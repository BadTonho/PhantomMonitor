using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace RivaMonitor.Services;

public sealed class FpsMonitor : IDisposable
{
    private TraceEventSession? _session;
    private readonly FrametimeBuffer _frametimeHistory = new(600);
    private readonly Dictionary<int, ProcessFpsData> _processes = new();
    private readonly List<double> _fpsSamples = new(300);

    private volatile float _fps;
    private volatile float _frametimeMs;
    private volatile float _fpsAvg;
    private volatile float _fpsMin = float.MaxValue;
    private volatile float _fps1Low;
    private volatile bool _hasData;
    private volatile string _debugInfo = "Inicializando...";
    private DateTime _lastStatsCalc = DateTime.UtcNow;
    private readonly string _sessionName;

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
        _sessionName = $"RivaMonitorFps_{Process.GetCurrentProcess().Id}";
    }

    public void Start()
    {
        if (_session != null) return;

        try
        {
            _session = new TraceEventSession(_sessionName);
            _session.EnableProvider("Microsoft-Windows-DXGI", TraceEventLevel.Informational);

            _session.Source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DXGI", "Present_Start", OnFrameEvent);

            Task.Run(() =>
            {
                try
                {
                    _session.Source.Process();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _debugInfo = $"ETW error: {ex.Message}";
                }
            });

            _debugInfo = "ETW ativo";
        }
        catch (UnauthorizedAccessException)
        {
            _debugInfo = "ETW: admin necessário";
        }
        catch (Exception ex)
        {
            _debugInfo = $"ETW init: {ex.Message}";
            _session?.Dispose();
            _session = null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private int GetForegroundPid()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid;
    }

    private void OnFrameEvent(TraceEvent evt)
    {
        try
        {
            var pid = (int)evt.ProcessID;
            if (pid <= 0) return;

            var now = DateTime.UtcNow;

            if (!_processes.TryGetValue(pid, out var proc))
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    proc = new ProcessFpsData { Name = p.ProcessName };
                }
                catch
                {
                    proc = new ProcessFpsData { Name = $"pid:{pid}" };
                }
                _processes[pid] = proc;
            }

            // Ignorar processos de sistema e outros que não são jogos
            var name = proc.Name.ToLowerInvariant();
            if (name == "dwm" || name == "explorer" || name == "rivamonitor" || 
                name == "chrome" || name == "msedge" || name == "discord" || 
                name == "devenv" || name == "code" || name == "taskmgr" || name == "steamwebhelper")
            {
                return; // Note: Removido ApplicationFrameHost para suportar UWP (Roblox Microsoft Store)
            }

            proc.FramesThisWindow++;

            var elapsed = (now - proc.LastCalc).TotalSeconds;
            if (elapsed >= 1.0)
            {
                var fps = proc.FramesThisWindow / elapsed;
                proc.FramesThisWindow = 0;
                proc.CurrentFps = fps;
                proc.LastCalc = now;

                ProcessFpsData? best = null;
                var fgPid = GetForegroundPid();

                if (_processes.TryGetValue(fgPid, out var fgProc) && fgProc.CurrentFps > 0)
                {
                    best = fgProc; // Prioridade absoluta para a janela em foco
                }
                else
                {
                    foreach (var kvp in _processes)
                    {
                        if (best == null || kvp.Value.CurrentFps > best.CurrentFps)
                            best = kvp.Value;
                    }
                }

                if (best != null && best.CurrentFps > 0)
                {
                    _fps = (float)best.CurrentFps;
                    _frametimeMs = _fps > 0 ? 1000f / _fps : 0;
                    _frametimeHistory.Add(_frametimeMs);
                    _fpsSamples.Add(_fps);
                    if (_fps < _fpsMin) _fpsMin = _fps;
                    _hasData = true;
                    _debugInfo = $"FPS: {_fps:F1} ({best.Name})";
                }

                if ((now - _lastStatsCalc).TotalSeconds >= 2.0 && _fpsSamples.Count > 0)
                {
                    _fpsAvg = (float)_fpsSamples.Average();
                    var sorted = _fpsSamples.OrderBy(v => v).ToList();
                    _fps1Low = (float)sorted[Math.Max(0, (int)(sorted.Count * 0.01))];
                    _lastStatsCalc = now;
                }
            }
        }
        catch { }
    }

    public void Reset()
    {
        _processes.Clear();
        _fpsSamples.Clear();
        _frametimeHistory.Clear();
        _fps = 0;
        _frametimeMs = 0;
        _fpsMin = float.MaxValue;
        _fpsAvg = 0;
        _fps1Low = 0;
        _hasData = false;
    }

    public void Dispose()
    {
        if (_session != null)
        {
            try { _session.Stop(); } catch { }
            try { _session.Dispose(); } catch { }
            _session = null;
        }
    }

    private sealed class ProcessFpsData
    {
        public string Name = "";
        public long FramesThisWindow;
        public double CurrentFps;
        public DateTime LastCalc = DateTime.UtcNow;
    }
}
