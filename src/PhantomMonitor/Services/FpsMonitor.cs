using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PhantomMonitor.Services;

public sealed class FpsMonitor : IFpsMonitor
{
    private const int MaxSamples = 600;
    private readonly FrametimeBuffer _frametimeHistory = new(MaxSamples);
    private readonly Queue<double> _fpsSamples = new(MaxSamples);
    private readonly object _lock = new();

    private Process? _presentMonProcess;
    private readonly Timer _pollTimer;

    private volatile float _fps;
    private volatile float _fpsAvg;
    private volatile float _fpsMin = float.MaxValue;
    private volatile float _fps1Low;
    private volatile float _lastFrametimeMs;
    private volatile bool _hasData;
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
    private bool _headerParsed;

    // EMA (Exponential Moving Average) para suavizar o FPS
    private double _emaFps;
    private bool _emaInitialized;
    private const double EmaAlpha = 0.35;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public float Fps => _fps;
    public float FrametimeMs => _lastFrametimeMs > 0 ? _lastFrametimeMs : (_fps > 0 ? 1000f / _fps : 0);
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
        var candidates = new[]
        {
            Path.Combine(baseDir, "PresentMon.exe"),
            Path.Combine(baseDir, "tools", "PresentMon.exe"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "tools", "PresentMon.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "tools", "PresentMon.exe")),
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
            // Verifica se é a linha de cabeçalho
            if (!_headerParsed || line.StartsWith("Application", StringComparison.OrdinalIgnoreCase))
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
                if (_fpsSamples.Count > MaxSamples)
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
            _fps = 0;
            _fpsMin = float.MaxValue;
            _fpsAvg = 0;
            _fps1Low = 0;
            _lastFrametimeMs = 0;
            _hasData = false;
            _emaFps = 0;
            _emaInitialized = false;
        }
    }

    public void Reset()
    {
        ResetStats();
        _targetPid = 0;
        _targetProcessName = "";
    }

    public void Stop() => StopPresentMon();

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
