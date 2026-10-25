using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using static PhantomMonitor.Services.Win32Interop;

namespace PhantomMonitor.Services;

[SupportedOSPlatform("windows")]
public sealed class FpsMonitor : IFpsMonitor
{
    private const int MaxGraphSamples = 600;   // janela do mini-gráfico de frametime
    private const int MaxStatsSamples = 3600;  // janela das estatísticas (~60 s rodando a 60 FPS)
    private readonly FrametimeBuffer _frametimeHistory = new(MaxGraphSamples);
    private readonly Queue<double> _fpsSamples = new(MaxStatsSamples);
    private readonly Queue<double> _frametimeSamples = new(MaxStatsSamples); // janela p/ percentis puros
    private readonly object _lock = new();
    private readonly object _presentMonLock = new();
    private readonly PresentMonCsvParser _csvParser = new();
    private readonly RecentPresentingProcessTracker _presentingProcesses = new();
    private readonly PresentMonSessionDemand _sessionDemand = new();

    private Process? _presentMonProcess;
    private readonly Timer _pollTimer;

    private volatile float _fps;
    private volatile float _fpsAvg;
    private volatile float _fpsMin = float.MaxValue;
    private volatile float _fps1Low;
    private volatile float _fps01Low;
    private volatile float _fpsMax;
    private volatile float _lastFrametimeMs;
    private volatile bool _hasData;
    private volatile float _gpuBusy;        // colunas opcionais do PresentMon 2.x (0 = indisponível)
    private volatile float _cpuBusy;
    private volatile float _cpuWait;
    private volatile float _presentLatencyMs;
    private volatile float _fpsP1;
    private volatile float _fpsP99;
    private volatile float _frametimeP1;
    private volatile float _frametimeP99;
    private volatile string _debugInfo = "Inicializando...";
    private volatile string _perfResult = "";
    private volatile string _etwDiag = "Iniciando PresentMon...";

    private volatile int _targetPid;
    private volatile string _targetProcessName = "";
    private DateTime _lastFrameTime = DateTime.UtcNow;
    private DateTime _lastStatsCalc = DateTime.UtcNow;

    // EMA (Exponential Moving Average) para suavizar o FPS
    private double _emaFps;
    private bool _emaInitialized;
    private const double EmaAlpha = 0.35;

    // GetForegroundWindow/GetWindowThreadProcessId vivem em Win32Interop (mesma pasta).

    public float Fps => _fps;
    public float FrametimeMs => _lastFrametimeMs > 0 ? _lastFrametimeMs : (_fps > 0 ? 1000f / _fps : 0);
    public float FpsAvg => _fpsAvg;
    public float FpsMin => _fpsMin == float.MaxValue ? 0 : _fpsMin;
    public float Fps1Low => _fps1Low;
    public float Fps01Low => _fps01Low;
    public float FpsMax => _fpsMax;
    public float GpuBusy => _gpuBusy;
    public float CpuBusy => _cpuBusy;
    public float CpuWait => _cpuWait;
    public float PresentLatencyMs => _presentLatencyMs;
    public float FpsP1 => _fpsP1;
    public float FpsP99 => _fpsP99;
    public float FrametimeP1 => _frametimeP1;
    public float FrametimeP99 => _frametimeP99;
    public bool HasData => _hasData;
    public FrametimeBuffer FrametimeHistory => _frametimeHistory;
    public string DebugInfo => _debugInfo;
    public string PerfResult => _perfResult;
    public string EtwDiag => _etwDiag;

    public FpsMonitor()
    {
        _pollTimer = new Timer(_ => CheckForegroundWindow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _debugInfo = "Iniciando PresentMon...";
        Reset();
        _sessionDemand.SetCollectionActive(true);
        EnsurePresentMonState();
        _pollTimer.Change(0, 500);
    }

    public bool OverlayProcessDiscoveryEnabled
    {
        get => _sessionDemand.DiscoveryActive;
        set
        {
            _sessionDemand.SetDiscoveryActive(value);
            if (!value) _presentingProcesses.Clear();
            EnsurePresentMonState();
        }
    }

    public IReadOnlyList<PresentingProcess> GetRecentPresentingProcesses() =>
        _presentingProcesses.Snapshot(DateTimeOffset.UtcNow);

    private void EnsurePresentMonState()
    {
        lock (_presentMonLock)
        {
            if (_sessionDemand.IsRequired)
            {
                if (_presentMonProcess is null || IsPresentMonExited()) StartPresentMonCore();
            }
            else StopPresentMonCore();
        }
    }

    private bool IsPresentMonExited()
    {
        try { return _presentMonProcess?.HasExited ?? true; }
        catch (InvalidOperationException) { return true; }
    }

    private static string? FindPresentMonPath()
    {
        var baseDir = AppContext.BaseDirectory;
        // A partir do bin/Debug/net10.0-windows, a raiz do repositório fica 5 níveis acima
        // (bin/Debug/net10.0-windows → bin/Debug → bin → PhantomMonitor → src → raiz).
        // Os caminhos antigos de fallback resolviam para diretórios que não existem.
        var candidates = new[]
        {
            Path.Combine(baseDir, "PresentMon.exe"),
            Path.Combine(baseDir, "tools", "PresentMon.exe"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "tools", "PresentMon.exe")),
            Path.Combine(Directory.GetCurrentDirectory(), "tools", "PresentMon.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "PresentMon.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private void StartPresentMonCore()
    {
        try
        {
            StopPresentMonCore();
            _csvParser.Reset();

            var presentMonPath = FindPresentMonPath();
            if (presentMonPath == null)
            {
                _etwDiag = "ERRO: PresentMon.exe não encontrado.";
                _debugInfo = _etwDiag;
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = presentMonPath,
                // PresentMon 2.x: --no_track_gpu_video não existe (rejeitado como opção desconhecida).
                Arguments = "--output_stdout --no_console_stats --stop_existing_session --session_name PhantomMonitorFps --exclude PhantomMonitor.exe --no_track_input --no_track_gpu --exclude_dropped",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            var permissionErrorLogged = 0;
            _presentMonProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            _presentMonProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    ProcessCsvLine(e.Data);
                }
            };

            _presentMonProcess.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;

                var line = e.Data.Trim();
                if (line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Use --session_name", StringComparison.OrdinalIgnoreCase))
                    return;

                var permissionFailure = line.Contains("access denied", StringComparison.OrdinalIgnoreCase);
                if (permissionFailure)
                {
                    _etwDiag = "Requer Administrador ou membro do grupo Performance Log Users";
                    if (Interlocked.Exchange(ref permissionErrorLogged, 1) == 0)
                    {
                        ErrorLog.Write("PresentMon",
                            "A captura de FPS via ETW foi bloqueada por permissão. Execute como Administrador ou adicione a conta ao grupo Performance Log Users.");
                    }
                    return;
                }

                _etwDiag = $"PresentMon: {line}";
                ErrorLog.Write("PresentMon", line);
            };

            _presentMonProcess.Exited += (s, e) =>
            {
                // Preserva a causa (ex.: falta de privilégio); caso contrário informa o encerramento.
                if (!_etwDiag.StartsWith("Requer") && !_etwDiag.StartsWith("PresentMon:"))
                {
                    _etwDiag = "PresentMon encerrado";
                }
            };

            _presentMonProcess.Start();
            _etwDiag = "PresentMon ativo";
            _debugInfo = "PresentMon aguardando frames...";
            _presentMonProcess.BeginOutputReadLine();
            _presentMonProcess.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            _etwDiag = $"Erro ao iniciar PresentMon: {ex.Message}";
            _debugInfo = _etwDiag;
        }
    }

    private void ProcessCsvLine(string line)
    {
        try
        {
            if (!_csvParser.TryParseLine(line, out var frame) || frame is null) return;
            var pid = frame.ProcessId;
            var appName = frame.Application;
            if (_sessionDemand.DiscoveryActive && !WindowsOverlayProcessFilter.IsIgnored(appName))
                _presentingProcesses.Observe(pid, appName, DateTimeOffset.UtcNow);

            // A mesma sessão ETW continua servindo à descoberta com a coleta parada.
            if (!_sessionDemand.CollectionActive) return;
            int targetPid = _targetPid;
            if (targetPid > 0 && pid != targetPid) return;

            double ftMs = frame.FrametimeMs;
            double gpuBusy = frame.GpuBusy;
            double cpuBusy = frame.CpuBusy;
            double cpuWait = frame.CpuWait;
            double latency = frame.DisplayLatencyMs;

            if (targetPid == 0 && appName.Length > 0 && !WindowsOverlayProcessFilter.IsIgnored(appName))
            {
                _targetPid = pid;
                _targetProcessName = appName;
            }
            lock (_lock)
            {
                _lastFrametimeMs = (float)ftMs;
                _frametimeHistory.Add((float)ftMs);
                _frametimeSamples.Enqueue(ftMs);
                if (_frametimeSamples.Count > MaxStatsSamples)
                {
                    _frametimeSamples.Dequeue();
                }

                _gpuBusy = (float)gpuBusy;
                _cpuBusy = (float)cpuBusy;
                _cpuWait = (float)cpuWait;
                _presentLatencyMs = (float)latency;

                double instantFps = 1000.0 / ftMs;

                if (!_emaInitialized)
                {
                    _emaFps = instantFps;
                    _emaInitialized = true;
                }
                else
                {
                    _emaFps = EmaAlpha * instantFps + (1.0 - EmaAlpha) * _emaFps;
                }

                _fps = (float)_emaFps;
                _fpsSamples.Enqueue(instantFps);
                if (_fpsSamples.Count > MaxStatsSamples)
                {
                    _fpsSamples.Dequeue();
                }

                _hasData = true;
                _lastFrameTime = DateTime.UtcNow;

                var now = DateTime.UtcNow;
                if ((now - _lastStatsCalc).TotalSeconds >= 1.0 && _fpsSamples.Count > 0)
                {
                    var sorted = _fpsSamples.OrderBy(v => v).ToList();
                    _fpsAvg = (float)sorted.Average();
                    // Mínimo da janela recente (e não do início da sessão, sensível a um único pico).
                    _fpsMin = (float)sorted[0];
                    int idx1Low = Math.Max(0, (int)(sorted.Count * 0.01));
                    _fps1Low = (float)sorted[idx1Low];
                    int idx01Low = Math.Max(0, (int)(sorted.Count * 0.001));
                    _fps01Low = (float)sorted[idx01Low];
                    _fpsMax = (float)sorted[^1];

                    // Percentis puros (P6.5): P1/P99 de FPS e de frametime, na mesma
                    // janela — o 1% Low existente é só P1 da janela de FPS; aqui o
                    // FrametimeP99 expõe os spikes ms-brutos que o gráfico já mostra.
                    _fpsP1 = (float)PercentileOf(sorted, 0.01);
                    _fpsP99 = (float)PercentileOf(sorted, 0.99);
                    if (_frametimeSamples.Count > 0)
                    {
                        var sortedFt = _frametimeSamples.OrderBy(v => v).ToList();
                        _frametimeP1 = (float)PercentileOf(sortedFt, 0.01);
                        _frametimeP99 = (float)PercentileOf(sortedFt, 0.99);
                    }

                    _lastStatsCalc = now;
                }

                _perfResult = $"{_fps:F0} FPS ({ftMs:F1} ms) - {_targetProcessName} (PID {_targetPid})";
                _debugInfo = _perfResult;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("FpsMonitor.ProcessCsvLine", ex);
        }
    }

    private void CheckForegroundWindow()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out uint fgPid);
                if (fgPid > 0)
                {
                    string pName = "";
                    try
                    {
                        using var p = Process.GetProcessById((int)fgPid);
                        pName = p.ProcessName;
                    }
                    catch { pName = $"pid:{fgPid}"; }

                    if (!WindowsOverlayProcessFilter.IsIgnored(pName) && _targetPid != (int)fgPid)
                    {
                        _targetPid = (int)fgPid;
                        _targetProcessName = pName;
                        ResetStats();
                    }
                }
            }

            // Se o jogo parou de enviar frames há mais de 2 segundos, zera o FPS exibido
            if ((DateTime.UtcNow - _lastFrameTime).TotalSeconds > 2.0 && _hasData)
            {
                lock (_lock)
                {
                    _fps = 0;
                    _lastFrametimeMs = 0;
                    _perfResult = $"Aguardando frames de {_targetProcessName}...";
                    _debugInfo = _perfResult;
                }
            }
        }
        catch (Exception ex) { ErrorLog.Write("FpsMonitor.CheckForegroundWindow", ex); }
    }

    private void ResetStats()
    {
        lock (_lock)
        {
            _fpsSamples.Clear();
            _frametimeHistory.Clear();
            _frametimeSamples.Clear();
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
            _gpuBusy = 0;
            _cpuBusy = 0;
            _cpuWait = 0;
            _presentLatencyMs = 0;
            _fpsP1 = 0;
            _fpsP99 = 0;
            _frametimeP1 = 0;
            _frametimeP99 = 0;
        }
    }

    /// <summary>Percentil p puro sobre lista já ordenada (índice inferior, padrão nearest-rank).</summary>
    private static double PercentileOf(List<double> sortedLower, double p) =>
        sortedLower[Math.Clamp((int)Math.Floor(sortedLower.Count * p), 0, sortedLower.Count - 1)];

    public void Reset()
    {
        ResetStats();
        _targetPid = 0;
        _targetProcessName = "";
    }

    public void Stop()
    {
        // Além de encerrar o PresentMon, para o polling da janela ativa: sem isso,
        // CheckForegroundWindow continuava rodando a cada 500 ms com o monitor parado.
        try { _pollTimer.Change(Timeout.Infinite, Timeout.Infinite); }
        catch (ObjectDisposedException) { /* Dispose em corrida com Stop */ }
        _sessionDemand.SetCollectionActive(false);
        EnsurePresentMonState();
    }

    private void StopPresentMon()
    {
        lock (_presentMonLock) StopPresentMonCore();
    }

    private void StopPresentMonCore()
    {
        if (_presentMonProcess != null)
        {
            try
            {
                if (!_presentMonProcess.HasExited)
                {
                    _presentMonProcess.Kill(true);
                    _presentMonProcess.WaitForExit(1000);
                }
            }
            catch { }
            finally
            {
                _presentMonProcess.Dispose();
                _presentMonProcess = null;
            }
        }
    }

    public void Dispose()
    {
        _sessionDemand.SetCollectionActive(false);
        _sessionDemand.SetDiscoveryActive(false);
        _presentingProcesses.Clear();
        _pollTimer.Dispose();
        StopPresentMon();
    }
}
