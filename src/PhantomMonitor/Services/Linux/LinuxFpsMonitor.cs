using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RivaMonitor.Services.Linux;

/// <summary>
/// Estrutura binária de telemetria compartilhada entre o RivaVulkanLayer (nativo C++)
/// e o RivaMonitor via memória compartilhada (/dev/shm/riva_fps ou /tmp/riva_fps.bin).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RivaVulkanTelemetryHeader
{
    public const uint ExpectedMagic = 0x52495641; // "RIVA"

    public uint Magic;
    public uint Version;
    public uint Pid;
    public ulong FrameCount;
    public ulong TimestampNs;
    public float FrametimeMs;
    public float InstantFps;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string ProcessName;
}

/// <summary>
/// Monitor de FPS nativo para Linux que consome telemetria do RivaVulkanLayer
/// diretamente da memória compartilhada sem intermediários ou aplicações de terceiros.
/// </summary>
public sealed class LinuxFpsMonitor : IFpsMonitor
{
    private readonly FrametimeBuffer _frametimeHistory = new(600);
    private readonly List<double> _fpsSamples = new(600);
    private readonly object _lock = new();

    private readonly Timer _pollTimer;
    private readonly string[] _sharedMemoryPaths =
    [
        "/dev/shm/riva_fps",
        "/tmp/riva_fps.bin"
    ];

    private volatile float _fps;
    private volatile float _fpsAvg;
    private volatile float _fpsMin = float.MaxValue;
    private volatile float _fps1Low;
    private volatile float _lastFrametimeMs;
    private volatile bool _hasData;
    private volatile string _debugInfo = "Aguardando RivaVulkanLayer...";

    private ulong _lastFrameCount;
    private uint _lastPid;
    private string _currentProcessName = "";
    private DateTime _lastFrameReceivedTime = DateTime.UtcNow;
    private DateTime _lastStatsCalc = DateTime.UtcNow;

    // EMA (Exponential Moving Average)
    private double _emaFps;
    private bool _emaInitialized;
    private const double EmaAlpha = 0.35;

    public float Fps => _fps;
    public float FrametimeMs => _lastFrametimeMs > 0 ? _lastFrametimeMs : (_fps > 0 ? 1000f / _fps : 0);
    public float FpsAvg => _fpsAvg;
    public float FpsMin => _fpsMin == float.MaxValue ? 0 : _fpsMin;
    public float Fps1Low => _fps1Low;
    public bool HasData => _hasData;
    public FrametimeBuffer FrametimeHistory => _frametimeHistory;
    public string DebugInfo => _debugInfo;

    public LinuxFpsMonitor()
    {
        _pollTimer = new Timer(_ => PollTelemetry(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        Reset();
        _pollTimer.Change(0, 100);
    }

    public void Stop()
    {
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Reset()
    {
        lock (_lock)
        {
            _fpsSamples.Clear();
            _frametimeHistory.Clear();
            _fps = 0;
            _fpsMin = float.MaxValue;
            _fpsAvg = 0;
            _fps1Low = 0;
            _lastFrametimeMs = 0;
            _hasData = false;
            _emaFps = 0;
            _emaInitialized = false;
            _lastFrameCount = 0;
            _lastPid = 0;
            _currentProcessName = "";
            _debugInfo = "Aguardando RivaVulkanLayer...";
        }
    }

    private void PollTelemetry()
    {
        try
        {
            string? activePath = null;
            foreach (var path in _sharedMemoryPaths)
            {
                if (File.Exists(path))
                {
                    activePath = path;
                    break;
                }
            }

            if (activePath == null)
            {
                if (_hasData && (DateTime.UtcNow - _lastFrameReceivedTime).TotalSeconds > 2.0)
                {
                    Reset();
                }
                return;
            }

            // Ler cabeçalho estruturado
            byte[] buffer = new byte[Marshal.SizeOf<RivaVulkanTelemetryHeader>()];
            using (var fs = new FileStream(activePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read = fs.Read(buffer, 0, buffer.Length);
                if (read < buffer.Length) return;
            }

            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            RivaVulkanTelemetryHeader telemetry;
            try
            {
                telemetry = Marshal.PtrToStructure<RivaVulkanTelemetryHeader>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }

            if (telemetry.Magic != RivaVulkanTelemetryHeader.ExpectedMagic)
            {
                return;
            }

            lock (_lock)
            {
                // Se trocou de processo alvo
                if (telemetry.Pid != _lastPid && telemetry.Pid > 0)
                {
                    _lastPid = telemetry.Pid;
                    _currentProcessName = string.IsNullOrWhiteSpace(telemetry.ProcessName)
                        ? $"pid:{telemetry.Pid}"
                        : telemetry.ProcessName;
                    _fpsSamples.Clear();
                    _frametimeHistory.Clear();
                    _fpsMin = float.MaxValue;
                    _emaInitialized = false;
                }

                // Houve apresentação de novo quadro
                if (telemetry.FrameCount > _lastFrameCount)
                {
                    _lastFrameCount = telemetry.FrameCount;
                    _lastFrameReceivedTime = DateTime.UtcNow;
                    _hasData = true;

                    float ftMs = telemetry.FrametimeMs;
                    float instantFps = telemetry.InstantFps;

                    if (ftMs > 0 && ftMs < 1000f)
                    {
                        _lastFrametimeMs = ftMs;
                        _frametimeHistory.Add(ftMs);
                    }

                    if (instantFps > 0 && instantFps < 2000f)
                    {
                        if (!_emaInitialized)
                        {
                            _emaFps = instantFps;
                            _emaInitialized = true;
                        }
                        else
                        {
                            _emaFps = (EmaAlpha * instantFps) + ((1.0 - EmaAlpha) * _emaFps);
                        }

                        _fps = (float)Math.Round(_emaFps, 1);
                        _fpsSamples.Add(instantFps);

                        if (_fpsSamples.Count > 600)
                            _fpsSamples.RemoveAt(0);

                        if (instantFps < _fpsMin)
                            _fpsMin = instantFps;
                    }

                    var now = DateTime.UtcNow;
                    if ((now - _lastStatsCalc).TotalSeconds >= 1.0 && _fpsSamples.Count > 0)
                    {
                        _fpsAvg = (float)_fpsSamples.Average();
                        var sorted = _fpsSamples.OrderBy(v => v).ToList();
                        int idx1Low = Math.Max(0, (int)(sorted.Count * 0.01));
                        _fps1Low = (float)sorted[idx1Low];
                        _lastStatsCalc = now;
                    }

                    _debugInfo = $"{_fps:F0} FPS ({_lastFrametimeMs:F1} ms) - {_currentProcessName} (PID {_lastPid}) [Vulkan]";
                }
                else if ((DateTime.UtcNow - _lastFrameReceivedTime).TotalSeconds > 2.0 && _hasData)
                {
                    _fps = 0;
                    _lastFrametimeMs = 0;
                    _debugInfo = $"Aguardando frames de {_currentProcessName}...";
                }
            }
        }
        catch
        {
            // Proteção contra leitura concorrente transitória
        }
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
    }
}
