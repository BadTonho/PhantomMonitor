using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RivaMonitor.Services;

public sealed class FpsMonitor : IFpsMonitor
{
    private readonly FrametimeBuffer _frametimeHistory = new(600);
    private readonly List<double> _fpsSamples = new(600);
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

    private int _targetPid;
    private string _targetProcessName = "";
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
                Arguments = "--output_stdout --no_console_stats --stop_existing_session --session_name RivaMonitorFps --exclude RivaMonitor.exe --no_track_input --no_track_gpu_video --exclude_dropped",
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
                }
            };

            _presentMonProcess.Exited += (s, e) =>
            {
                if (string.IsNullOrEmpty(_etwDiag) || _etwDiag.StartsWith("PresentMon ativo"))
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
                var headers = line.Split(',');
                for (int i = 0; i < headers.Length; i++)
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

            var parts = line.Split(',');
            int minCols = Math.Max(_colPid, _colFrametime);
            if (parts.Length <= minCols) return;

            if (!int.TryParse(parts[_colPid].Trim(), out int pid) || pid <= 0)
                return;

            // Filtro por processo: focar no processo da janela ativa se definido
            if (_targetPid > 0 && pid != _targetPid)
                return;

            string rawFt = parts[_colFrametime].Trim();
            if (!double.TryParse(rawFt, NumberStyles.Float, CultureInfo.InvariantCulture, out double ftMs) || ftMs <= 0 || ftMs > 2000)
                return;

            // Se ainda não temos um target e o processo não é do sistema, adotamos este
            if (_targetPid == 0 && _colApp >= 0 && parts.Length > _colApp)
            {
                var appName = parts[_colApp].Trim();
                if (!IsIgnoredProcess(appName))
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
                _fpsSamples.Add(instantFps);
                if (_fpsSamples.Count > 600)
                {
                    _fpsSamples.RemoveAt(0);
                }

                if (instantFps > 0 && instantFps < _fpsMin)
                {
                    _fpsMin = (float)instantFps;
                }

                _hasData = true;
                _lastFrameTime = DateTime.UtcNow;

                var now = DateTime.UtcNow;
                if ((now - _lastStatsCalc).TotalSeconds >= 1.0 && _fpsSamples.Count > 0)
                {
                    _fpsAvg = (float)_fpsSamples.Average();
                    var sorted = _fpsSamples.OrderBy(v => v).ToList();
                    int idx1Low = Math.Max(0, (int)(sorted.Count * 0.01));
                    _fps1Low = (float)sorted[idx1Low];
                    _lastStatsCalc = now;
                }

                _perfResult = $"{_fps:F0} FPS ({ftMs:F1} ms) - {_targetProcessName} (PID {_targetPid})";
                _debugInfo = _perfResult;
            }
        }
        catch { }
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
                        var p = Process.GetProcessById((int)fgPid);
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
        catch { }
    }

    private static bool IsIgnoredProcess(string name)
    {
        return name.Equals("RivaMonitor", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("LockApp", StringComparison.OrdinalIgnoreCase);
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
