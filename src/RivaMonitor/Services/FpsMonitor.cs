using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RivaMonitor.Services;

public sealed class FpsMonitor : IDisposable
{
    private readonly FrametimeBuffer _frametimeHistory = new(600);
    private readonly List<double> _fpsSamples = new(300);
    private DateTime _lastStatsCalc = DateTime.UtcNow;
    private DateTime _lastFpsCalc = DateTime.UtcNow;
    private readonly Timer _timer;

    private volatile float _fps;
    private volatile float _fpsAvg;
    private volatile float _fpsMin = float.MaxValue;
    private volatile float _fps1Low;
    private volatile bool _hasData;
    private volatile string _debugInfo = "Inicializando...";
    private volatile string _perfResult = "";
    private volatile string _etwDiag = "";
    private volatile int _pollIteration;

    private IntPtr _dxgiOutputPtr;
    private long _lastPresentCount;
    private GetFrameStatsDelegate? _getFrameStats;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1([MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppFactory);

    private static readonly Guid IID_IDXGIFactory1 = new("770AAE78-F26F-4DBA-A829-253C83D1B387");
    private static readonly Guid IID_IDXGIAdapter1 = new("29038F61-3839-4626-91FD-086879011A05");
    private static readonly Guid IID_IDXGIOutput = new("AE02EEDB-C735-4690-8D52-5A8DC20213AA");

    private delegate int GetFrameStatsDelegate(IntPtr pThis, out DxgiFrameStatistics stats);

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiFrameStatistics
    {
        public uint PresentCount;
        public uint PresentRefreshCount;
        public uint SyncRefreshCount;
        public long SyncQPCTime;
        public long SyncGPUTime;
    }

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
        _debugInfo = "Iniciando...";
        InitDxgiOutput();
        _timer.Change(0, 500);
    }

    private void InitDxgiOutput()
    {
        try
        {
            var hr = CreateDXGIFactory1(IID_IDXGIFactory1, out var factoryPtr);
            if (hr < 0 || factoryPtr == IntPtr.Zero) { _etwDiag = $"CreateDXGIFactory1: {hr}"; return; }

            for (int ai = 0; ; ai++)
            {
                var enumAdapters = GetVtblMethod<EnumAdapters1Delegate>(factoryPtr, 12);
                hr = enumAdapters(factoryPtr, (uint)ai, out var adapterPtr);
                if (hr < 0 || adapterPtr == IntPtr.Zero) break;

                for (int oi = 0; ; oi++)
                {
                    var enumOutputs = GetVtblMethod<EnumOutputsDelegate>(adapterPtr, 7);
                    hr = enumOutputs(adapterPtr, (uint)oi, out var outputPtr);
                    if (hr < 0 || outputPtr == IntPtr.Zero) break;

                    var getDesc = GetVtblMethod<GetDescDelegate>(outputPtr, 7);
                    hr = getDesc(outputPtr, out var desc);
                    if (hr >= 0 && (desc.Right - desc.Left) > 0)
                    {
                        _dxgiOutputPtr = outputPtr;
                        _getFrameStats = GetVtblMethod<GetFrameStatsDelegate>(outputPtr, 18);
                        _etwDiag = $"DXGI: {desc.DeviceName}";
                        return;
                    }
                    Marshal.Release(outputPtr);
                }
                Marshal.Release(adapterPtr);
            }
            _etwDiag = "DXGI: nenhum output";
        }
        catch (Exception ex)
        {
            _etwDiag = $"DXGI init: {ex.Message}";
        }
    }

    private static T GetVtblMethod<T>(IntPtr comPtr, int methodIndex) where T : class
    {
        var vtbl = Marshal.ReadIntPtr(comPtr);
        var methodPtr = Marshal.ReadIntPtr(vtbl, IntPtr.Size * methodIndex);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(methodPtr, typeof(T));
    }

    private delegate int EnumAdapters1Delegate(IntPtr factory, uint adapterIndex, out IntPtr adapter);
    private delegate int EnumOutputsDelegate(IntPtr adapter, uint outputIndex, out IntPtr output);
    private delegate int GetDescDelegate(IntPtr output, out DxgiOutputDesc desc);

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiOutputDesc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int AttachedToDesktop;
        public uint Rotation;
        public IntPtr Monitor;
    }

    private void Poll()
    {
        _pollIteration++;
        try
        {
            var hwnd = GetForegroundWindow();
            string fgName = "";
            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out uint fgPid);
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

            if (_dxgiOutputPtr != IntPtr.Zero && _getFrameStats != null)
            {
                var hr = _getFrameStats(_dxgiOutputPtr, out var stats);
                if (hr >= 0)
                {
                    var current = (long)stats.PresentCount;
                    if (_lastPresentCount > 0)
                    {
                        var delta = (uint)(current - _lastPresentCount);
                        var now = DateTime.UtcNow;
                        var elapsed = (now - _lastFpsCalc).TotalSeconds;
                        if (elapsed >= 0.5)
                        {
                            var fps = delta / elapsed;
                            _fps = (float)fps;
                            _frametimeHistory.Add(FrametimeMs);
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
                            _lastFpsCalc = now;
                        }
                    }
                    _lastPresentCount = current;
                    _perfResult = $"#{_pollIteration} {_fps:F0} FPS, foco: {fgName}";
                    _debugInfo = _perfResult;
                }
                else
                {
                    _perfResult = $"#{_pollIteration} DXGI: erro {hr}";
                    _debugInfo = _perfResult;
                }
            }
            else
            {
                _perfResult = $"#{_pollIteration} DXGI: sem output, foco: {fgName}";
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
        _fpsSamples.Clear();
        _frametimeHistory.Clear();
        _fps = 0;
        _fpsMin = float.MaxValue;
        _fpsAvg = 0;
        _fps1Low = 0;
        _hasData = false;
    }

    public void Dispose()
    {
        _timer.Dispose();
        if (_dxgiOutputPtr != IntPtr.Zero) Marshal.Release(_dxgiOutputPtr);
    }
}
