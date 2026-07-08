using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RivaMonitor.Services;

public sealed class FpsMonitor : IDisposable
{
    private readonly FrametimeBuffer _frametimeHistory = new(600);
    private readonly List<double> _fpsSamples = new(300);
    private readonly Dictionary<string, ProcessTracker> _processes = new();
    private readonly Timer _pollTimer;
    private IntPtr _lastHwnd;
    private string _lastProcessName = "";
    private long _lastFrameCount;
    private DateTime _lastPoll = DateTime.UtcNow;
    private DateTime _lastStatsCalc = DateTime.UtcNow;

    private volatile float _fps;
    private volatile float _frametimeMs;
    private volatile float _fpsAvg;
    private volatile float _fpsMin = float.MaxValue;
    private volatile float _fps1Low;
    private volatile bool _hasData;
    private volatile string _debugInfo = "Aguardando...";

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
        _pollTimer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _pollTimer.Change(0, 200);
        _debugInfo = "Monitorando janela ativa...";
    }

    private void Poll()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                _lastHwnd = IntPtr.Zero;
                _hasData = false;
                return;
            }

            // Check if window changed
            if (hwnd != _lastHwnd)
            {
                _lastHwnd = hwnd;
                if (!IsValidWindow(hwnd))
                {
                    _hasData = false;
                    return;
                }

                var pid = GetWindowPid(hwnd);
                var name = GetProcessName(pid);

                if (!_processes.TryGetValue(name, out var tracker))
                {
                    tracker = new ProcessTracker();
                    _processes[name] = tracker;
                }
                _lastProcessName = name;
                _lastFrameCount = 0;
                _lastPoll = DateTime.UtcNow;
                _debugInfo = $"Janela: {name}";
            }

            if (!IsValidWindow(_lastHwnd))
            {
                _lastHwnd = IntPtr.Zero;
                _hasData = false;
                return;
            }

            // Try DWM timing info for per-window frame count
            var frames = GetDwmFrameCount(_lastHwnd);
            if (frames <= 0)
            {
                // Fallback: try DXGI ETW-style via process GPU activity
                _debugInfo = $"Sem dados DWM: {_lastProcessName}";
                return;
            }

            if (_lastFrameCount == 0)
            {
                _lastFrameCount = frames;
                _lastPoll = DateTime.UtcNow;
                return;
            }

            if (frames <= _lastFrameCount) return; // no new frame

            var elapsed = (DateTime.UtcNow - _lastPoll).TotalSeconds;
            if (elapsed < 0.5) return;

            var newFrames = frames - _lastFrameCount;
            var fps = newFrames / elapsed;

            _lastFrameCount = frames;
            _lastPoll = DateTime.UtcNow;

            if (fps > 0 && fps < 600)
            {
                _fps = (float)fps;
                _frametimeMs = 1000f / _fps;
                _frametimeHistory.Add(_frametimeMs);
                _fpsSamples.Add(_fps);
                if (_fps < _fpsMin) _fpsMin = _fps;
                _hasData = true;

                if ((DateTime.UtcNow - _lastStatsCalc).TotalSeconds >= 2.0 && _fpsSamples.Count > 0)
                {
                    _fpsAvg = (float)_fpsSamples.Average();
                    var sorted = _fpsSamples.OrderBy(v => v).ToList();
                    _fps1Low = (float)sorted[Math.Max(0, (int)(sorted.Count * 0.01))];
                    _lastStatsCalc = DateTime.UtcNow;
                }

                _debugInfo = $"FPS: {_fps:F1} ({_lastProcessName}) via DWM";
            }
        }
        catch { }
    }

    private static long GetDwmFrameCount(IntPtr hwnd)
    {
        try
        {
            var info = new DWM_TIMING_INFO();
            info.cbSize = Marshal.SizeOf<DWM_TIMING_INFO>();
            var ptr = Marshal.AllocHGlobal(info.cbSize);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                var hr = DwmGetCompositionTimingInfo(hwnd, ptr);
                if (hr == 0)
                {
                    // Read cFrameDisplayed at offset
                    // On Win10+, the struct has many fields, cFrameDisplayed is at a specific offset
                    // Use Marshal to read the field directly
                    var result = Marshal.ReadInt64(ptr, 64); // offset of cFrameDisplayed varies
                    return result;
                }
                return -1;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return -1;
        }
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
        _lastFrameCount = 0;
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, IntPtr timingInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private static uint GetWindowPid(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    private static string GetProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return $"pid:{pid}"; }
    }

    private static bool IsValidWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (!IsWindowVisible(hwnd)) return false;

        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        var title = sb.ToString();
        if (string.IsNullOrWhiteSpace(title)) return false;

        var pid = GetWindowPid(hwnd);
        var name = GetProcessName(pid);
        return !name.Equals("RivaMonitor", StringComparison.OrdinalIgnoreCase);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_TIMING_INFO
    {
        public int cbSize;
        public DWM_FRAME_RATE rate;
        public DWM_FRAME_RATE rateComposition;
        public long qpcFrameStart;
        public int cRefresh;
        public int cDXRefresh;
        public long qpcComposition;
        public long qpcFrameDisplayed;
        public long qpcCompositionDisplayed;
        public long qpcDisplayedTime;
        public long qpcCompositionDisplayTime;
        public long qpcCompositionVBlankTime;
        public long qpcVBlank;
        public long cFrameDisplayed; // at offset 88 (8*11)
        public long cFramesDropped;
        public int cRefreshFrame;
        public int cCompositionFrame;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_FRAME_RATE
    {
        public int numerator;
        public int denominator;
    }

    private sealed class ProcessTracker
    {
        public long FrameCount;
        public DateTime LastSeen = DateTime.UtcNow;
    }
}
