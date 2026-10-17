using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace RivaMonitor.Services;

public sealed class FpsMonitor : IDisposable
{
    private readonly FrametimeBuffer _frametimeHistory = new(600);
    private readonly List<double> _fpsSamples = new(300);
    private readonly object _samplesLock = new();
    private DateTime _lastStatsCalc = DateTime.UtcNow;
    private readonly Timer _timer;

    // Usa Stopwatch para timing de alta precisão
    private readonly Stopwatch _stopwatch = new();
    private long _lastFpsCalcTick;

    private volatile float _fps;
    private volatile float _fpsAvg;
    private volatile float _fpsMin = float.MaxValue;
    private volatile float _fps1Low;
    private volatile bool _hasData;
    private volatile string _debugInfo = "Inicializando...";
    private volatile string _perfResult = "";
    private volatile string _etwDiag = "";
    private volatile int _pollIteration;

    private TraceEventSession? _session;
    private readonly ConcurrentDictionary<int, long> _pidPresentCounts = new();
    private int _lastFgPid;
    private long _lastPresentCount;

    // EMA (Exponential Moving Average) para suavizar o FPS
    private double _emaFps;
    private bool _emaInitialized;
    private const double EmaAlpha = 0.3; // 0.0 = muito suave, 1.0 = sem suavização

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public float Fps => _fps;
    public float FrametimeMs => _fps > 0 ? 1000f / _fps : 0;
    public float FpsAvg => _fpsAvg;
    public float FpsMin => _fpsMin == float.MaxValue ? 0 : _fpsMin;
    public float Fps1Low => _fps1Low;
    public bool HasData => _hasData;
    public FrametimeBuffer FrametimeHistory => _frametimeHistory;
    public string DebugInfo => _debugInfo;
    public string PerfResult => _perfResult;
    public string EtwDiag => _etwDiag;

    public FpsMonitor()
    {
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _debugInfo = "Iniciando ETW...";
        _stopwatch.Start();
        _lastFpsCalcTick = _stopwatch.ElapsedTicks;
        InitEtw();
        _timer.Change(0, 500);
    }

    private void InitEtw()
    {
        try
        {
            if (TraceEventSession.IsElevated() != true)
            {
                _etwDiag = "ERRO: Requer Administrador para ler FPS via ETW.";
                return;
            }

            _session = new TraceEventSession("RivaMonitorFpsSession");
            _session.StopOnDispose = true;

            // Ativa os provedores de gráficos:
            // - DxgKrnl: captura Present de qualquer API gráfica (DX, OpenGL, Vulkan)
            // - DXGI: captura Present de aplicações DirectX
            _session.EnableProvider("Microsoft-Windows-DxgKrnl", TraceEventLevel.Informational, ulong.MaxValue);
            _session.EnableProvider("Microsoft-Windows-DXGI", TraceEventLevel.Informational, ulong.MaxValue);

            // Usa HashSet para deduplicar: cada frame pode gerar eventos em ambos os provedores
            // Contamos apenas do DxgKrnl (mais abrangente), DXGI serve como fallback
            var dxgiPids = new ConcurrentDictionary<int, long>();

            _session.Source.Dynamic.All += (TraceEvent data) =>
            {
                int pid = data.ProcessID;
                if (pid == 0) return;

                string providerName = data.ProviderName;
                string eventName = data.EventName;

                // FILTRO EXATO: contar apenas o evento "Present" (1 por frame)
                // Ignora: PresentHistory, PresentHistoryDetailed, PresentMonitoredFence, etc.
                if (eventName == "Present")
                {
                    if (providerName == "Microsoft-Windows-DxgKrnl")
                    {
                        _pidPresentCounts.AddOrUpdate(pid, 1, (_, count) => count + 1);
                    }
                    else if (providerName == "Microsoft-Windows-DXGI")
                    {
                        // Conta DXGI separado como fallback
                        dxgiPids.AddOrUpdate(pid, 1, (_, count) => count + 1);
                    }
                }
            };

            // Fallback: se DxgKrnl não tiver dados para um PID, usa DXGI
            // Isso é verificado periodicamente no Poll()
            Task.Run(() =>
            {
                try { _session.Source.Process(); }
                catch { /* Sessão encerrada */ }
            });

            // Timer para mesclar contadores DXGI como fallback
            var mergeTimer = new Timer(_ =>
            {
                foreach (var kvp in dxgiPids)
                {
                    // Só usa DXGI se DxgKrnl não tem dados para esse PID
                    if (!_pidPresentCounts.ContainsKey(kvp.Key) || _pidPresentCounts[kvp.Key] == 0)
                    {
                        _pidPresentCounts[kvp.Key] = kvp.Value;
                    }
                }
            }, null, 1000, 2000);

            _etwDiag = "ETW Session OK (DxgKrnl + DXGI)";
        }
        catch (Exception ex)
        {
            _etwDiag = $"ETW init ERRO: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void Poll()
    {
        _pollIteration++;
        try
        {
            var hwnd = GetForegroundWindow();
            string fgName = "";
            uint fgPid = 0;

            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out fgPid);
                if (fgPid > 0)
                {
                    try
                    {
                        var p = Process.GetProcessById((int)fgPid);
                        fgName = p.ProcessName;
                    }
                    catch { fgName = $"pid:{fgPid}"; }

                    if (fgName.Equals("RivaMonitor", StringComparison.OrdinalIgnoreCase))
                    {
                        _perfResult = $"#{_pollIteration} foco: RivaMonitor";
                        _debugInfo = _perfResult;
                        return;
                    }
                }
            }

            if (_session != null)
            {
                if (fgPid > 0)
                {
                    if (fgPid != _lastFgPid)
                    {
                        _lastFgPid = (int)fgPid;
                        _lastPresentCount = _pidPresentCounts.TryGetValue(_lastFgPid, out var startC) ? startC : 0;
                        Reset();
                        _lastFpsCalcTick = _stopwatch.ElapsedTicks;
                    }

                    long current = _pidPresentCounts.TryGetValue(_lastFgPid, out var cur) ? cur : 0;
                    var delta = current - _lastPresentCount;
                    long nowTick = _stopwatch.ElapsedTicks;
                    double elapsed = (double)(nowTick - _lastFpsCalcTick) / Stopwatch.Frequency;

                    if (elapsed >= 0.4 && delta > 0)
                    {
                        double rawFps = delta / elapsed;

                        // Suavização EMA para evitar oscilações
                        if (!_emaInitialized)
                        {
                            _emaFps = rawFps;
                            _emaInitialized = true;
                        }
                        else
                        {
                            _emaFps = EmaAlpha * rawFps + (1.0 - EmaAlpha) * _emaFps;
                        }

                        _fps = (float)_emaFps;
                        _frametimeHistory.Add(FrametimeMs);

                        lock (_samplesLock)
                        {
                            _fpsSamples.Add(_fps);
                            if (_fps > 0 && _fps < _fpsMin) _fpsMin = _fps;
                            _hasData = true;

                            var now = DateTime.UtcNow;
                            if ((now - _lastStatsCalc).TotalSeconds >= 2.0 && _fpsSamples.Count > 0)
                            {
                                _fpsAvg = (float)_fpsSamples.Average();
                                var sorted = _fpsSamples.OrderBy(v => v).ToList();
                                _fps1Low = (float)sorted[Math.Max(0, (int)(sorted.Count * 0.01))];
                                _lastStatsCalc = now;
                            }
                        }
                        _lastPresentCount = current;
                        _lastFpsCalcTick = nowTick;
                    }
                    _perfResult = $"#{_pollIteration} {_fps:F0} FPS (raw={delta / Math.Max(elapsed, 0.001):F0}, delta={delta}, elapsed={elapsed:F3}s), foco: {fgName}";
                    _debugInfo = _perfResult;
                }
                else
                {
                    _perfResult = $"#{_pollIteration} ETW: Sem processo em foco";
                    _debugInfo = _perfResult;
                }
            }
            else
            {
                _perfResult = $"#{_pollIteration} ETW: não iniciado ({_etwDiag}), foco: {fgName}";
                _debugInfo = _perfResult;
            }
        }
        catch (Exception ex)
        {
            _perfResult = $"#{_pollIteration} erro: {ex.GetType().Name}: {ex.Message}";
            _debugInfo = _perfResult;
        }
    }

    public void Reset()
    {
        lock (_samplesLock)
        {
            _fpsSamples.Clear();
        }
        _frametimeHistory.Clear();
        _fps = 0;
        _fpsMin = float.MaxValue;
        _fpsAvg = 0;
        _fps1Low = 0;
        _hasData = false;
        _emaFps = 0;
        _emaInitialized = false;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _stopwatch.Stop();
        if (_session != null)
        {
            try
            {
                _session.Dispose();
            }
            catch { }
        }
    }
}
