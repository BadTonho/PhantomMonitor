using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
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

    // Colunas dinâmicas do CSV do PresentMon
    private int _colPid = -1;
    private int _colApp = -1;
    private int _colFrametime = -1;

    // Colunas opcionais (present tracking estendido): quando o executável/versão não
    // as emite, os índices ficam em -1 e as métricas publicam 0 ("indisponível").
    private int _colGpuBusy = -1;
    private int _colCpuBusy = -1;
    private int _colCpuWait = -1;
    private int _colDisplayLatency = -1;
    private bool _headerParsed;

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
        StartPresentMon();
        _pollTimer.Change(0, 500);
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

    private void StartPresentMon()
    {
        try
        {
            StopPresentMon();

            // Colunas descobertas na sessão anterior não podem vazar para o novo CSV
            // (o formato do header pode diferir entre sessões/versões do PresentMon).
            _headerParsed = false;
            _colPid = -1;
            _colApp = -1;
            _colFrametime = -1;
            _colGpuBusy = -1;
            _colCpuBusy = -1;
            _colCpuWait = -1;
            _colDisplayLatency = -1;

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
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

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
                if (!string.IsNullOrEmpty(e.Data))
                {
                    if (e.Data.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("elevated privilege", StringComparison.OrdinalIgnoreCase))
                    {
                        _etwDiag = "Requer Administrador (execute como Admin)";
                    }
                    else if (!e.Data.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        _etwDiag = $"PresentMon: {e.Data}";
                    }
                    ErrorLog.Write("PresentMon", e.Data);
                }
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
            _presentMonProcess.BeginOutputReadLine();
            _presentMonProcess.BeginErrorReadLine();

            _etwDiag = "PresentMon ativo";
            _debugInfo = "PresentMon aguardando frames...";
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
            // Cabeçalho: chega no início da sessão (e re-emite quando ela reinicia).
            // Trata como header apenas linhas que CONTÊM as colunas esperadas — um jogo
            // cujo processo se chama "Application.exe" (padrão do Unreal) gerava linhas
            // de dados começando com "Application" e re-parseava o header no meio da
            // sessão, desalinhando os índices de coluna.
            if (!_headerParsed || LooksLikeHeader(line))
            {
                var headers = SplitCsv(line);
                for (int i = 0; i < headers.Count; i++)
                {
                    var h = headers[i].Trim();
                    if (h.Equals("ProcessID", StringComparison.OrdinalIgnoreCase))
                        _colPid = i;
                    else if (h.Equals("Application", StringComparison.OrdinalIgnoreCase))
                        _colApp = i;
                    else if (h.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase) ||
                             h.Equals("FrameTime", StringComparison.OrdinalIgnoreCase) ||
                             h.Equals("msBetweenDisplayChange", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_colFrametime == -1 || h.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase))
                            _colFrametime = i;
                    }

                    // Colunas opcionais, casadas por substring case-insensitive porque o
                    // PresentMon varia o nome entre versões ("GpuBusy%", "GPU Busy %".
                    // "MsUntilDisplayed"...). Colunas ausentes = índice permanece -1 e as
                    // métricas publicam 0 (indicativo de indisponível) sem quebrar o resto.
                    else if (_colGpuBusy == -1 && h.Contains("gpubusy", StringComparison.OrdinalIgnoreCase))
                        _colGpuBusy = i;
                    else if (_colCpuBusy == -1 && h.Contains("cpubusy", StringComparison.OrdinalIgnoreCase))
                        _colCpuBusy = i;
                    else if (_colCpuWait == -1 && h.Contains("cpuwait", StringComparison.OrdinalIgnoreCase))
                        _colCpuWait = i;
                    else if (_colDisplayLatency == -1 &&
                             (h.Contains("msuntildisplayed", StringComparison.OrdinalIgnoreCase) ||
                              h.Contains("presentlatency", StringComparison.OrdinalIgnoreCase)))
                        _colDisplayLatency = i;
                }

                if (_colPid >= 0 && _colFrametime >= 0)
                {
                    _headerParsed = true;
                }
                return;
            }

            var parts = SplitCsv(line);
            int minCols = Math.Max(_colPid, _colFrametime);
            if (parts.Count <= minCols) return;

            if (!int.TryParse(parts[_colPid].Trim(), out int pid) || pid <= 0)
                return;

            int targetPid = _targetPid;

            // Filtro por processo: focar no processo da janela ativa se definido
            if (targetPid > 0 && pid != targetPid)
                return;

            string rawFt = parts[_colFrametime].Trim();
            if (!double.TryParse(rawFt, NumberStyles.Float, CultureInfo.InvariantCulture, out double ftMs) || ftMs <= 0 || ftMs > 2000)
                return;

            // Métricas extras (colunas opcionais): só entram se o header declarou a
            // coluna e o valor for plausível — linhas antigas/outra versão publicam 0.
            double gpuBusy = ParseOptional(parts, _colGpuBusy, 0, 100);
            double cpuBusy = ParseOptional(parts, _colCpuBusy, 0, 100);
            double cpuWait = ParseOptional(parts, _colCpuWait, 0, 100);
            double latency = ParseOptional(parts, _colDisplayLatency, 0, 2000);

            // Se ainda não temos um target e o processo não é do sistema, adotamos este
            if (targetPid == 0 && _colApp >= 0 && parts.Count > _colApp)
            {
                var appName = parts[_colApp].Trim();
                if (!IsIgnoredProcess(Path.GetFileNameWithoutExtension(appName)))
                {
                    _targetPid = pid;
                    _targetProcessName = appName;
                }
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

    private static bool LooksLikeHeader(string line)
    {
        return line.Contains("ProcessID", StringComparison.OrdinalIgnoreCase) &&
               (line.Contains("msBetweenPresents", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("FrameTime", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Divide uma linha CSV respeitando campos entre aspas.</summary>
    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result;
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

                    if (!IsIgnoredProcess(pName) && _targetPid != (int)fgPid)
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

    private static bool IsIgnoredProcess(string name)
    {
        return name.Equals("PhantomMonitor", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("LockApp", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("dwm", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("devenv", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Coluna opcional: -1 = ausente, valor ruins = 0 ("indisponível"); range plausível protege de lixo.</summary>
    private static double ParseOptional(List<string> parts, int col, double min, double max)
    {
        if (col < 0 || col >= parts.Count) return 0;
        return double.TryParse(parts[col].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
               v >= min && v <= max ? v : 0;
    }

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
        StopPresentMon();
    }

    private void StopPresentMon()
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
        _pollTimer.Dispose();
        StopPresentMon();
    }
}
