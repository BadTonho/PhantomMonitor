using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PhantomMonitor.Services.Linux;

/// <summary>
/// Estrutura binária de telemetria compartilhada entre o PhantomVulkanLayer (nativo C++)
/// e o PhantomMonitor via memória compartilhada (/dev/shm/phantom_fps_&lt;pid&gt; ou
/// /tmp/phantom_fps_&lt;pid&gt;.bin — um objeto por processo Vulkan com a layer ativa).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PhantomVulkanTelemetryHeader
{
    public const uint ExpectedMagic = 0x50484E54; // "PHNT"

    public uint Magic;
    public uint Version;
    public uint Pid;
    public ulong FrameCount;
    public ulong TimestampNs;
    public float FrametimeMs;
    public float InstantFps;

    // ByValTStr sem CharSet = Ansi; no .NET/CoreCLR Unix Ansi resolve para UTF-8 —
    // exatamente os bytes UTF-8 gravados pela layer via /proc/self/comm.
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string ProcessName;
}

/// <summary>
/// Monitor de FPS nativo para Linux que consome telemetria do PhantomVulkanLayer
/// diretamente da memória compartilhada sem intermediários ou aplicações de terceiros.
/// </summary>
public sealed class LinuxFpsMonitor : IFpsMonitor
{
    private const int MaxStatsSamples = 3600; // janela das estatísticas (~60 s rodando a 60 FPS)
    private readonly FrametimeBuffer _frametimeHistory = new(600);
    private readonly Queue<double> _fpsSamples = new(MaxStatsSamples);
    private readonly object _lock = new();

    private readonly Timer _pollTimer;

    // A layer cria UM objeto de SHM por processo: /dev/shm/phantom_fps_<pid> (ou
    // /tmp/phantom_fps_<pid>.bin no fallback). Enumerar e validar o PID vivo evita
    // "FPS fantasma" do processo anterior (a layer agora faz shm_unlink, mas o
    // leitor também se defende de objetos órfãos de versões antigas/crash).
    private static readonly string[] ShmDirectories = ["/dev/shm", "/tmp"];

    private volatile float _fps;
    private volatile float _fpsAvg;
    private volatile float _fpsMin = float.MaxValue;
    private volatile float _fps1Low;
    private volatile float _fps01Low;
    private volatile float _fpsMax;
    private volatile float _lastFrametimeMs;
    private volatile bool _hasData;
    private volatile string _debugInfo = "Aguardando PhantomVulkanLayer...";

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
    public float Fps01Low => _fps01Low;
    public float FpsMax => _fpsMax;
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
            _fps01Low = 0;
            _fpsMax = 0;
            _lastFrametimeMs = 0;
            _hasData = false;
            _emaFps = 0;
            _emaInitialized = false;
            _lastFrameCount = 0;
            _lastPid = 0;
            _currentProcessName = "";
            _debugInfo = "Aguardando PhantomVulkanLayer...";
        }
    }

    private void PollTelemetry()
    {
        try
        {
            PhantomVulkanTelemetryHeader? telemetry = null;
            foreach (var path in EnumerateSharedMemoryPaths())
            {
                // Objeto válido = magic correto + PID autoral vivo (ou o PID que já
                // rastreamos, para tolerar o instante entre encerramento e reset).
                if (TryReadHeader(path, out var header) &&
                    (header.Pid == _lastPid || ProcessPidAlive(header.Pid)))
                {
                    telemetry = header;
                    break;
                }
            }

            if (telemetry == null)
            {
                if (_hasData && (DateTime.UtcNow - _lastFrameReceivedTime).TotalSeconds > 2.0)
                {
                    Reset();
                }
                return;
            }

            var head = telemetry.Value;
            lock (_lock)
            {
                // Se trocou de processo alvo
                if (head.Pid != _lastPid && head.Pid > 0)
                {
                    _lastPid = head.Pid;
                    _currentProcessName = string.IsNullOrWhiteSpace(head.ProcessName)
                        ? $"pid:{head.Pid}"
                        : head.ProcessName;
                    _fpsSamples.Clear();
                    _frametimeHistory.Clear();
                    _fpsMin = float.MaxValue;
                    _emaInitialized = false;
                }

                // Houve apresentação de novo quadro
                if (head.FrameCount > _lastFrameCount)
                {
                    _lastFrameCount = head.FrameCount;
                    _lastFrameReceivedTime = DateTime.UtcNow;
                    _hasData = true;

                    float ftMs = head.FrametimeMs;
                    float instantFps = head.InstantFps;

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
                        _fpsSamples.Enqueue(instantFps);

                        if (_fpsSamples.Count > MaxStatsSamples)
                            _fpsSamples.Dequeue();

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
                        int idx01Low = Math.Max(0, (int)(sorted.Count * 0.001));
                        _fps01Low = (float)sorted[idx01Low];
                        _fpsMax = (float)sorted[^1];
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

    private static IEnumerable<string> EnumerateSharedMemoryPaths()
    {
        foreach (var dir in ShmDirectories)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "phantom_fps*");
            }
            catch
            {
                continue; // diretório inacessível: segue para o próximo
            }

            foreach (var file in files)
                yield return file;
        }
    }

    private static bool ProcessPidAlive(uint pid)
    {
        if (pid == 0) return false;
        try
        {
            return Directory.Exists($"/proc/{pid}");
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadHeader(string path, out PhantomVulkanTelemetryHeader header)
    {
        header = default;
        try
        {
            var size = Marshal.SizeOf<PhantomVulkanTelemetryHeader>();
            byte[] buffer = new byte[size];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read = 0;
                while (read < size)
                {
                    int n = fs.Read(buffer, read, size - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < size) return false;
            }

            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                header = Marshal.PtrToStructure<PhantomVulkanTelemetryHeader>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }

            return header.Magic == PhantomVulkanTelemetryHeader.ExpectedMagic;
        }
        catch
        {
            // Leitura concorrente transitória (a layer reescreve a cada frame): tenta no próximo ciclo
            return false;
        }
    }
}
