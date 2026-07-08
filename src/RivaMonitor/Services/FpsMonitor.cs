using System.Runtime.InteropServices;

namespace RivaMonitor.Services;

public sealed class FpsMonitor : IDisposable
{
    private const int DWMWA_LAST_FRAME_PRESENT_TIME = 34;

    private readonly FrametimeBuffer _frametimeHistory = new(600);
    private long _lastFrameQpc;
    private double _fps;
    private double _frametimeMs;
    private double _fpsAvg;
    private double _fpsMin = double.MaxValue;
    private double _fps1Low;
    private bool _hasData;
    private long _qpcFrequency;
    private DateTime _lastStatsReset = DateTime.UtcNow;
    private readonly List<double> _recentFpsValues = new(300);
    private string _debugInfo = "";

    public float Fps => (float)_fps;
    public float FrametimeMs => (float)_frametimeMs;
    public float FpsAvg => (float)_fpsAvg;
    public float FpsMin => (float)(_fpsMin == double.MaxValue ? 0 : _fpsMin);
    public float Fps1Low => (float)_fps1Low;
    public bool HasData => _hasData;
    public FrametimeBuffer FrametimeHistory => _frametimeHistory;
    public string DebugInfo => _debugInfo;

    public FpsMonitor()
    {
        QueryPerformanceFrequency(out _qpcFrequency);
    }

    public void Poll()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                _debugInfo = "No foreground window";
                _hasData = false;
                return;
            }

            // Get window title
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title))
            {
                _debugInfo = "Foreground window has no title";
                _hasData = false;
                return;
            }

            // Try DwmGetWindowAttribute with LAST_FRAME_PRESENT_TIME
            var hr = DwmGetWindowAttribute(hwnd, DWMWA_LAST_FRAME_PRESENT_TIME,
                out long frameQpc, sizeof(long));

            if (hr != 0)
            {
                _debugInfo = $"DwmGetWindowAttribute returned {hr} for '{title}'";
                _hasData = false;
                return;
            }

            if (frameQpc == 0)
            {
                _debugInfo = $"Frame QPC is 0 for '{title}'";
                _hasData = false;
                return;
            }

            if (_lastFrameQpc == 0)
            {
                // First frame detected
                _lastFrameQpc = frameQpc;
                _debugInfo = $"First frame detected for '{title}'";
                _hasData = false;
                return;
            }

            if (frameQpc != _lastFrameQpc)
            {
                var prevQpc = _lastFrameQpc;
                _lastFrameQpc = frameQpc;

                var delta = frameQpc - prevQpc;
                if (delta > 0 && delta < _qpcFrequency * 10)
                {
                    _frametimeMs = (double)delta / _qpcFrequency * 1000.0;
                    _fps = _frametimeMs > 0 ? 1000.0 / _frametimeMs : 0;
                    _frametimeHistory.Add((float)_frametimeMs);

                    if (_fps > 0 && _fps < 500)
                    {
                        _recentFpsValues.Add(_fps);
                        if (_fps < _fpsMin) _fpsMin = _fps;
                    }

                    _debugInfo = $"FPS: {_fps:F1} | FT: {_frametimeMs:F1}ms | '{title}'";
                    _hasData = true;
                }
                else
                {
                    _debugInfo = $"Frame delta {delta} too large for '{title}'";
                }
            }
            else
            {
                _debugInfo = $"Same frame QPC for '{title}'";
            }

            // Recalculate stats every 2 seconds
            var elapsed = (DateTime.UtcNow - _lastStatsReset).TotalSeconds;
            if (elapsed >= 2.0 && _recentFpsValues.Count > 0)
            {
                _fpsAvg = _recentFpsValues.Average();
                var sorted = _recentFpsValues.OrderBy(v => v).ToList();
                var onePctIdx = Math.Max(0, (int)(sorted.Count * 0.01));
                _fps1Low = sorted[onePctIdx];
                _lastStatsReset = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _debugInfo = $"Error: {ex.Message}";
            _hasData = false;
        }
    }

    public void Reset()
    {
        _lastFrameQpc = 0;
        _recentFpsValues.Clear();
        _fps = 0;
        _frametimeMs = 0;
        _fpsMin = double.MaxValue;
        _fpsAvg = 0;
        _fps1Low = 0;
        _hasData = false;
        _frametimeHistory.Clear();
    }

    public void Dispose() { }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out long pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("kernel32.dll")]
    private static extern bool QueryPerformanceFrequency(out long lpFrequency);
}
