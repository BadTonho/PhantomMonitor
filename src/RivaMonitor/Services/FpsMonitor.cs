using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace RivaMonitor.Services;

public sealed class FpsMonitor : IDisposable
{
    private TraceEventSession? _session;
    private readonly FrametimeBuffer _frametimeHistory = new(600);
    private readonly List<double> _fpsSamples = new(300);
    private DateTime _lastStatsCalc = DateTime.UtcNow;
    private int _frameCount;
    private DateTime _lastFpsCalc = DateTime.UtcNow;
    private readonly Timer _timer;

    private volatile float _fps;
    private volatile float _frametimeMs;
    private volatile float _fpsAvg;
    private volatile float _fpsMin = float.MaxValue;
    private volatile float _fps1Low;
    private volatile bool _hasData;
    private volatile string _debugInfo = "Inicializando...";
    private volatile int _etwEventCount;
    private volatile string _perfResult = "";
    private volatile string _etwDiag = "";
    private volatile int _pollIteration;

    private static readonly Guid DXGKRNL_GUID = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private static readonly Guid DWM_CORE_GUID = new("9E9BA3C0-F4E5-4FA6-B120-201E4F81F2D2");

    private volatile int _targetPid;
    private volatile int _presentEventId;
    private readonly Dictionary<int, int> _eventCounts = new();
    private DateTime _calibrationStart;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public float Fps => _fps;
    public float FrametimeMs => _frametimeMs;
    public float FpsAvg => _fpsAvg;
    public float FpsMin => _fpsMin == float.MaxValue ? 0 : _fpsMin;
    public float Fps1Low => _fps1Low;
    public bool HasData => _hasData;
    public FrametimeBuffer FrametimeHistory => _frametimeHistory;
    public string DebugInfo => _debugInfo;
    public int EtwEventCount => _etwEventCount;
    public string PerfResult => _perfResult;
    public string EtwDiag => _etwDiag;

    public FpsMonitor()
    {
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _debugInfo = "Iniciando...";
        StartEtw();
        _timer.Change(0, 500);
    }

    private void StartEtw()
    {
        foreach (var guid in new[] { DXGKRNL_GUID, DWM_CORE_GUID })
        {
            try
            {
                var session = new TraceEventSession("RivaMonitorFps");
                session.EnableProvider(guid, TraceEventLevel.Verbose, 0x0001);
                session.Source.AllEvents += OnAnyEvent;

                Task.Run(() =>
                {
                    try { session.Source.Process(); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { _etwDiag = $"ETW proc: {ex.Message}"; }
                });

                _session = session;
                _etwDiag = $"ETW ativo: {guid}";
                _debugInfo = "ETW: aguardando eventos";
                return;
            }
            catch (UnauthorizedAccessException)
            {
                _etwDiag = "Admin necessário";
            }
            catch (Exception ex)
            {
                _etwDiag = $"Init: {ex.Message}";
            }
        }
    }

    private void OnAnyEvent(TraceEvent evt)
    {
        try
        {
            var eid = 0;
            try { eid = (int)evt.ID; } catch { }
            var tp = _targetPid;
            var ep = 0;
            try { ep = evt.ProcessID; } catch { }
            if (tp > 0 && ep == tp)
            {
                var pid = _presentEventId;
                if (pid > 0)
                {
                    if (eid == pid)
                    {
                        Interlocked.Increment(ref _etwEventCount);
                        AddFrames(1, "ETW");
                    }
                }
                else
                {
                    _eventCounts.TryGetValue(eid, out var n);
                    _eventCounts[eid] = n + 1;
                    AddFrames(1, "RAW");
                }
            }
        }
        catch { }
    }

    private void AddFrames(int count, string source)
    {
        Interlocked.Add(ref _frameCount, count);
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastFpsCalc).TotalSeconds;
        if (elapsed >= 0.5)
        {
            var total = Interlocked.Exchange(ref _frameCount, 0);
            var fps = total / elapsed;
            _fps = (float)fps;
            _frametimeMs = 1000f / _fps;
            _frametimeHistory.Add(_frametimeMs);
            _fpsSamples.Add(_fps);
            if (_fps < _fpsMin) _fpsMin = _fps;
            _hasData = true;

            if ((now - _lastStatsCalc).TotalSeconds >= 2.0 && _fpsSamples.Count > 0)
            {
                _fpsAvg = (float)_fpsSamples.Average();
                var sorted = _fpsSamples.OrderBy(v => v).ToList();
                _fps1Low = (float)sorted[Math.Max(0, (int)(sorted.Count * 0.01))];
                _lastStatsCalc = now;
            }

            if (_presentEventId == 0 && _targetPid > 0 && _eventCounts.Count > 0)
            {
                var totalSec = (now - _calibrationStart).TotalSeconds;
                if (totalSec >= 2.0)
                {
                    var bestId = 0;
                    var bestScore = double.MaxValue;
                    foreach (var kv in _eventCounts)
                    {
                        var rate = kv.Value / totalSec;
                        if (rate >= 15 && rate <= 400)
                        {
                            var score = Math.Abs(rate - 60);
                            if (score < bestScore) { bestScore = score; bestId = kv.Key; }
                        }
                    }
                    if (bestId > 0)
                    {
                        _presentEventId = bestId;
                        _etwDiag = $"calib: ID {bestId} = {_eventCounts[bestId] / totalSec:F1} FPS";
                    }
                    else
                    {
                        _etwDiag = $"calib: nenhum ID entre 15-400 FPS";
                        _eventCounts.Clear();
                        _calibrationStart = now;
                    }
                }
                else
                {
                    _etwDiag = $"calib: {_eventCounts.Count} IDs, restam {2 - totalSec:F1}s";
                }
            }
            else if (_presentEventId > 0)
            {
                _etwDiag = $"ID {_presentEventId}: {_fps:F0} FPS";
            }

            _debugInfo = $"FPS: {_fps:F0} ({source})";
            _lastFpsCalc = now;
        }
    }

    private void Poll()
    {
        _pollIteration++;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out uint fgPid);
                if (fgPid > 0)
                {
                    string fgName = "";
                    try
                    {
                        var p = Process.GetProcessById((int)fgPid);
                        fgName = p.ProcessName;
                    }
                    catch { fgName = $"pid:{fgPid}"; }

                    if (!fgName.Equals("RivaMonitor", StringComparison.OrdinalIgnoreCase))
                    {
                        var prev = _targetPid;
                        _targetPid = (int)fgPid;
                        if (prev != _targetPid)
                        {
                            _presentEventId = 0;
                            _eventCounts.Clear();
                            _calibrationStart = DateTime.UtcNow;
                        }
                        _perfResult = $"#{_pollIteration} foco: {fgName}({fgPid})";
                        _debugInfo = _perfResult;
                        return;
                    }
                }
            }
            _perfResult = $"#{_pollIteration} sem foco";
            _debugInfo = _perfResult;
        }
        catch (Exception ex)
        {
            _perfResult = $"#{_pollIteration} erro: {ex.GetType().Name}: {ex.Message}";
            _debugInfo = _perfResult;
        }
    }

    public void Reset()
    {
        _fpsSamples.Clear();
        _frametimeHistory.Clear();
        _fps = 0;
        _frametimeMs = 0;
        _fpsMin = float.MaxValue;
        _fpsAvg = 0;
        _fps1Low = 0;
        _hasData = false;
        _frameCount = 0;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _session?.Dispose();
    }
}
