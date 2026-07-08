using System.Diagnostics;
using System.Management;
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
    private readonly Timer _pollTimer;
    private string _lastProcessName = "";

    private volatile float _fps;
    private volatile float _frametimeMs;
    private volatile float _fpsAvg;
    private volatile float _fpsMin = float.MaxValue;
    private volatile float _fps1Low;
    private volatile bool _hasData;
    private volatile string _debugInfo = "Inicializando...";
    private volatile int _etwEventCount;
    private volatile string _lastWmiQuery = "";
    private volatile string _wmiResults = "";

    private static readonly Guid DWM_CORE = new("5E8B2363-FA5D-4370-86C4-7B36A0E6A6FC");
    private static readonly Guid DXGI = new("CA41C1A2-1A35-8E2A-2D39-9C6E6B5F2B1D");

    public float Fps => _fps;
    public float FrametimeMs => _frametimeMs;
    public float FpsAvg => _fpsAvg;
    public float FpsMin => _fpsMin == float.MaxValue ? 0 : _fpsMin;
    public float Fps1Low => _fps1Low;
    public bool HasData => _hasData;
    public FrametimeBuffer FrametimeHistory => _frametimeHistory;
    public string DebugInfo => _debugInfo;
    public int EtwEventCount => _etwEventCount;
    public string LastWmiQuery => _lastWmiQuery;
    public string WmiResults => _wmiResults;

    public FpsMonitor()
    {
        _pollTimer = new Timer(_ => PollFallback(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        try
        {
            _session = new TraceEventSession("RivaMonitorFps");
            _session.EnableProvider(DWM_CORE, TraceEventLevel.Verbose, 0xFFFFFFFFFFFFFFFF);
            _session.EnableProvider(DXGI, TraceEventLevel.Verbose, 0xFFFFFFFFFFFFFFFF);

            _session.Source.Dynamic.All += OnAnyEvent;

            Task.Run(() =>
            {
                try { _session.Source.Process(); }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _debugInfo = $"ETW error: {ex.Message}";
                }
            });

            _debugInfo = "ETW ativo";
            _pollTimer.Change(1000, 1000);
        }
        catch (UnauthorizedAccessException)
        {
            _debugInfo = "ETW: admin necessário";
            _pollTimer.Change(1000, 1000);
        }
        catch (Exception ex)
        {
            _debugInfo = $"ETW init: {ex.Message}";
            _pollTimer.Change(1000, 1000);
        }
    }

    private void CountFrame(string source)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastFpsCalc).TotalSeconds;

        var c = Interlocked.Increment(ref _frameCount);
        _debugInfo = $"[{c}] frames em {elapsed:F2}s ({source})";

        if (elapsed >= 1.0)
        {
            var count = Interlocked.Exchange(ref _frameCount, 0);
            var fps = count / elapsed;

            if (fps > 0 && fps < 600)
            {
                _fps = (float)fps;
                _frametimeMs = _fps > 0 ? 1000f / _fps : 0;
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

                _debugInfo = $"FPS: {_fps:F1} ({source})";
            }
            _lastFpsCalc = now;
        }
    }

    private void OnAnyEvent(TraceEvent evt)
    {
        try
        {
            var c = Interlocked.Increment(ref _etwEventCount);
            // Ignore non-DWM/DXGI providers
            if (evt.ProviderGuid != DWM_CORE && evt.ProviderGuid != DXGI) return;
            if (c <= 5)
                _debugInfo = $"ETW evt #{c}: PID={evt.ProcessID} Prov={evt.ProviderGuid} Op={evt.Opcode}";
            CountFrame(evt.ProviderGuid == DWM_CORE ? "DWM" : "DXGI");
        }
        catch { }
    }

    private void PollFallback()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid <= 0) return;

            string pname;
            try
            {
                var proc = Process.GetProcessById((int)pid);
                if (proc.ProcessName.Equals("RivaMonitor", StringComparison.OrdinalIgnoreCase))
                    return;
                pname = proc.ProcessName;
                _lastProcessName = pname;
            }
            catch { return; }

            var pidStr = pid.ToString();
            // Query ALL GPU engine counters and filter by PID in code (more reliable)
            using var allSearcher = new ManagementObjectSearcher(
                "SELECT Name, PercentOfTime FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            float usage = 0;
            int count = 0;
            var matchedNames = new System.Text.StringBuilder();
            var allNames = new System.Text.StringBuilder();
            foreach (var obj in allSearcher.Get())
            {
                var name = (string)obj["Name"] ?? "";
                if (allNames.Length < 200) allNames.Append(name).Append("  ");
                if (name.EndsWith("_" + pidStr) || name.StartsWith(pidStr + "_"))
                {
                    var pct = Convert.ToSingle(obj["PercentOfTime"] ?? 0);
                    if (matchedNames.Length < 100) matchedNames.Append($"{name}={pct:F1}% ");
                    usage += pct;
                    count++;
                }
            }

            _lastWmiQuery = $"ALL GPU engines (filtering for pid={pid})";
            _wmiResults = $"{pname}: {count} match, tot={usage:F1}% | matches: {matchedNames}| all: {allNames}";

            if (count > 0 && usage > 1.0f)
            {
                CountFrame(pname);
            }
        }
        catch (Exception ex)
        {
            _wmiResults = $"WMI error: {ex.GetType().Name}: {ex.Message}";
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
        _pollTimer.Dispose();
        if (_session != null)
        {
            try { _session.Stop(); } catch { }
            try { _session.Dispose(); } catch { }
            _session = null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
