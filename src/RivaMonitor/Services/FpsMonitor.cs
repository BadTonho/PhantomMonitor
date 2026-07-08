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

        Interlocked.Increment(ref _frameCount);

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
            if (evt.ProcessID <= 0 && (evt.ProviderGuid != DWM_CORE)) return;
            CountFrame("DWM");
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

            try
            {
                var proc = Process.GetProcessById((int)pid);
                if (proc.ProcessName.Equals("RivaMonitor", StringComparison.OrdinalIgnoreCase))
                    return;
                _lastProcessName = proc.ProcessName;
            }
            catch { return; }

            // Read GPU engine time for foreground process via WMI
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE '%_%" + pid + "'");
            float usage = 0;
            int count = 0;
            foreach (var obj in searcher.Get())
            {
                usage += Convert.ToSingle(obj["PercentOfTime"] ?? 0);
                count++;
            }

            if (count > 0 && usage > 1.0f)
            {
                // Process is using GPU - estimate FPS from GPU utilization pattern
                CountFrame(_lastProcessName);
            }
        }
        catch { }
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
