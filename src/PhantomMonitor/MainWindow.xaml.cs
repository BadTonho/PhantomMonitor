using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PhantomMonitor.Models;
using PhantomMonitor.Overlay;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Telemetry;

namespace PhantomMonitor;

public partial class MainWindow : Window
{
    private ISensorService? _sensorService;
    private OverlayWindow? _overlay;
    private CsvLogger? _csvLogger;
    private JsonLogger? _jsonLogger;
    private readonly HotkeyService _hotkeys = new();
    private TrayIconService? _tray;
    private AlertService? _alerts;
    private DateTime _startTime;
    private string _logDir = "";
    private AppConfig _config = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _config = AppConfig.Load(ConfigPaths.ConfigFile);

        _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDir);

        CsvToggle.IsChecked = _config.LogToCsv;
        JsonToggle.IsChecked = _config.LogToJson;
        OverlayToggle.IsChecked = _config.ShowOverlay;

        if (!IsRunningAsAdmin())
        {
            AdminWarning.Visibility = Visibility.Visible;
            StatusText.Text = "Clique em 'Executar como Admin'";
        }

        StartMonitoring();
        ApplyHotkey();
        ApplyAlertsConfig();
        InitTrayIcon();

        StartupToggle.IsChecked = StartupManager.IsEnabled();
    }

    /// <summary>Cria o ícone da bandeja (silencioso em caso de falha: o app continua funcional).</summary>
    private void InitTrayIcon()
    {
        try
        {
            _tray = new TrayIconService(this, "Phantom Monitor");
            _tray.ActivateRequested += () =>
            {
                Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            };
            _tray.OverlayToggleRequested += () =>
            {
                OverlayToggle.IsChecked = OverlayToggle.IsChecked != true;
            };
            _tray.StartupToggleRequested += () =>
            {
                StartupToggle.IsChecked = !StartupManager.IsEnabled();
            };
            _tray.ResetStatsRequested += () => ResetSeriesStats("bandeja");
            _tray.ExitRequested += Close;
            _tray.IsOverlayVisible = () => OverlayToggle.IsChecked == true;
            _tray.IsStartupEnabled = StartupManager.IsEnabled;
            // Seção dinâmica: perfis do OSD (fixar perfil / voltar ao automático).
            _tray.SetDynamicItems(() =>
            {
                var entries = new List<TrayIconService.MenuEntry>
                {
                    new((int)TrayIconService.MenuDynamicBase, "Automático (por aplicativo)",
                        _pinnedProfile == null)
                };
                for (int i = 0; i < _config.Profiles.Count; i++)
                {
                    var profile = _config.Profiles[i];
                    entries.Add(new(
                        (int)TrayIconService.MenuDynamicBase + i + 1,
                        profile.Name,
                        string.Equals(_pinnedProfile, profile.Name, StringComparison.OrdinalIgnoreCase)));
                }
                return entries;
            }, ConfigurePinnedProfile);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("TrayIcon.Init", ex);
        }
    }

    /// <summary>(Re)cria o avaliador de alertas conforme o config atual; popups aparecem no canto da tela.</summary>
    private void ApplyAlertsConfig()    {
        _alerts = new AlertService(_config.Alerts)
        {
            ShowPopup = (title, message) =>
                Dispatcher.InvokeAsync(() => AlertPopupWindow.ShowAlert(title, message))
        };
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; // ignora o ajuste inicial do estado do checkbox

        var want = StartupToggle.IsChecked == true;
        if (!StartupManager.SetEnabled(want))
        {
            ErrorLog.Write("StartupManager", $"Não foi possível {(want ? "ativar" : "desativar")} a inicialização com o Windows.");
        }

        // Reflete o estado real do registro (pode falhar silenciosamente em política restrita).
        var actual = StartupManager.IsEnabled();
        if (StartupToggle.IsChecked != actual)
            StartupToggle.IsChecked = actual;
    }

    /// <summary>(Re)registra o atalho global de mostrar/ocultar o OSD conforme o config atual.</summary>
    private void ApplyHotkey()
    {
        _hotkeys.Unregister();
        if (string.IsNullOrWhiteSpace(_config.ToggleOverlayHotkey)) return;

        _hotkeys.Register(this, _config.ToggleOverlayHotkey, () =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                OverlayToggle.IsChecked = OverlayToggle.IsChecked != true;
            });
        });
    }

    public static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private void OnRestartAsAdmin(object sender, RoutedEventArgs e)
    {
        var exePath = Environment.ProcessPath;
        if (exePath == null) return;

        var startInfo = new ProcessStartInfo(exePath)
        {
            Verb = "runas",
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory
        };

        try
        {
            Process.Start(startInfo);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("RestartAsAdmin", ex);
            MessageBox.Show("Não foi possível elevar o aplicativo. Execute manualmente como Administrador.",
                "Phantom Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StartMonitoring() => BeginMonitoring();

    // Perfil fixado pela bandeja (sessão; não persiste no config). null = automático.
    private string? _pinnedProfile;

    private void ConfigurePinnedProfile(int id)
    {
        if (id == (int)TrayIconService.MenuDynamicBase)
        {
            _pinnedProfile = null;
        }
        else
        {
            var index = id - (int)TrayIconService.MenuDynamicBase - 1;
            if (index < 0 || index >= _config.Profiles.Count) return;
            _pinnedProfile = _config.Profiles[index].Name;
        }
    }

    /// <summary>Estado do matcher de perfis consultado pelo overlay a cada tick de posição.</summary>
    private OverlayProfileState ResolveProfileState() =>
        new() { Profiles = _config.Profiles, Pinned = _pinnedProfile };

    /// <summary>
    /// Reset das estatísticas por série da sessão (min/max/avg), pedido pela bandeja
    /// ou pela janela de inventário. Quando o CSV está ativo, grava um marcador
    /// "#marker" para delimitar o segmento do reset na planilha.
    /// </summary>
    private void ResetSeriesStats(string source)
    {
        _sensorService?.ResetSeriesStats();
        _csvLogger?.WriteMarker($"reset ({source})");

        StatusText.Text = "Estatísticas resetadas";
        StatusText.Foreground = TryFindResource("TextSecondaryBrush") as Brush;
        StatusDot.Fill = TryFindResource("GreenBrush") as Brush;

        // Volta ao rótulo padrão após um instante (a fonte de verdade é Monitorando/Parado).
        var restore = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        restore.Tick += (_, _) =>
        {
            restore.Stop();
            if (_sensorService == null) return;
            StatusText.Text = "Monitorando";
            StatusText.Foreground = TryFindResource("GreenBrush") as Brush;
            StatusDot.Fill = TryFindResource("GreenBrush") as Brush;
        };
        restore.Start();
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => BeginMonitoring();

    private void BeginMonitoring()
    {
        if (_sensorService != null) return;

        _sensorService = SensorServiceFactory.Create(_config.UpdateIntervalMs);
        _sensorService.SensorUpdated += OnSensorData;

        _overlay = new OverlayWindow(_config,
            _config.OverlayPosition.X,
            _config.OverlayPosition.Y,
            ResolveProfileState);

        if (OverlayToggle.IsChecked == true)
            _overlay.Show();

        if (CsvToggle.IsChecked == true)
        {
            var path = Path.Combine(_logDir, $"sensors_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            _csvLogger = new CsvLogger(path);
        }

        if (JsonToggle.IsChecked == true)
        {
            var path = Path.Combine(_logDir, "sensors_latest.json");
            _jsonLogger = new JsonLogger(path);
        }

        // SDK de telemetria (memória compartilhada): sempre ativo junto ao monitoramento —
        // consumidores externos (OBS, AHK, dashboards) leem em tempo real sem parse.
        _telemetrySdk = new TelemetrySdkWriter();

        _startTime = DateTime.UtcNow;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "Monitorando";
        StatusText.Foreground = TryFindResource("GreenBrush") as Brush;
        StatusDot.Fill = TryFindResource("GreenBrush") as Brush;

        // Inicia o serviço por último, com loggers/overlay já prontos para o primeiro evento.
        _sensorService.Start();
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        if (_sensorService != null)
        {
            _sensorService.SensorUpdated -= OnSensorData;
            _sensorService.Dispose();
        }
        _overlay?.Close();
        _csvLogger?.Dispose();
        _jsonLogger?.Dispose();

        _sensorService = null;
        _overlay = null;
        _csvLogger = null;
        _jsonLogger = null;
        _telemetrySdk?.Dispose();
        _telemetrySdk = null;

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "Parado";
        StatusText.Foreground = TryFindResource("TextSecondaryBrush") as Brush;
        StatusDot.Fill = TryFindResource("TextMutedBrush") as Brush;
    }

    private void OnOverlayToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; // ignora o ajuste inicial do checkbox

        // O toggle agora persiste no config (antes mudava só o estado vivo e voltava
        // ao valor do config.json no próximo boot).
        _config.ShowOverlay = OverlayToggle.IsChecked == true;
        SaveConfig();

        if (_overlay == null) return;
        if (OverlayToggle.IsChecked == true)
        {
            if (!_overlay.IsVisible)
                _overlay.Show();
            _overlay.Visibility = Visibility.Visible;
        }
        else
        {
            _overlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnLoggingToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; // ignora o ajuste inicial dos checkboxes

        _config.LogToCsv = CsvToggle.IsChecked == true;
        _config.LogToJson = JsonToggle.IsChecked == true;
        SaveConfig();
        // Os loggers são (re)criados pelo BeginMonitoring na próxima sessão de monitoramento.
    }

    private void SaveConfig() => _config.Save(ConfigPaths.ConfigFile);

    private void OnSensorData(SensorData data)
    {
        if (Dispatcher.HasShutdownStarted) return;

        // Assíncrono: não bloqueia a thread do timer caso a UI esteja ocupada ou fechando.
        Dispatcher.InvokeAsync(() =>
        {
            if (_sensorService == null) return; // parado enquanto o evento estava na fila

            CpuUsageText.Text = $"{data.CpuUsage:F1}%";
            CpuUsageText.Foreground = GetUsageBrush(data.CpuUsage);
            CpuProgressBar.Value = Math.Clamp(data.CpuUsage, 0, 100);
            CpuRingText.Text = $"{data.CpuUsage:F0}%";
            CpuTempText.Text = data.CpuTemp > 0 ? $"{data.CpuTemp:F0}°C" : "--°C";
            CpuTempRingText.Text = data.CpuTemp > 0 ? $"{data.CpuTemp:F0}°C" : "--°C";
            UpdateTempRing(CpuTempRing, CpuTempRingText, data.CpuTemp);

            GpuUsageText.Text = $"{data.GpuUsage:F1}%";
            GpuUsageText.Foreground = GetUsageBrush(data.GpuUsage);
            GpuProgressBar.Value = Math.Clamp(data.GpuUsage, 0, 100);
            GpuRingText.Text = $"{data.GpuUsage:F0}%";
            GpuTempText.Text = data.GpuTemp > 0 ? $"{data.GpuTemp:F0}°C" : "--°C";
            GpuTempRingText.Text = data.GpuTemp > 0 ? $"{data.GpuTemp:F0}°C" : "--°C";
            UpdateTempRing(GpuTempRing, GpuTempRingText, data.GpuTemp);

            CpuDetailText.Text = SensorFormatter.BuildCpuDetail(data)
                ?? "Clock, potência e voltagem indisponíveis";
            GpuDetailText.Text = SensorFormatter.BuildGpuDetail(data)
                ?? "VRAM, clocks e hotspot indisponíveis";

            FpsText.Text = data.Fps > 0 ? $"{data.Fps:F0}" : "--";
            FpsDetailsText.Text = data.Fps > 0
                ? $"Mín: {data.FpsMin:F0} | Avg: {data.FpsAvg:F0} | 1% Low: {data.Fps1Low:F0} | 0.1% Low: {data.Fps01Low:F0} | Máx: {data.FpsMax:F0}"
                : "Mín: -- | Avg: -- | 1% Low: -- | 0.1% Low: -- | Máx: --";
            FrametimeText.Text = data.FrametimeMs > 0 ? $"{data.FrametimeMs:F1} ms" : "-- ms";

            RamUsageText.Text = $"{data.RamUsagePercent:F1}%";
            RamUsageText.Foreground = GetUsageBrush(data.RamUsagePercent);
            RamProgressBar.Value = Math.Clamp(data.RamUsagePercent, 0, 100);
            RamRingText.Text = $"{data.RamUsagePercent:F0}%";
            RamAvailableText.Text = data.RamAvailableGB > 0
                ? $"{data.RamAvailableGB:F1} GB disponível"
                : "-- GB disponível";
            var ramTotal = data.GetValue(SensorType.RamTotal);
            RamTotalText.Text = ramTotal > 0
                ? $"Total: {ramTotal:F1} GB"
                : "";

            var diskVal = data.GetValue(SensorType.DiskUsage);
            DiskUsageText.Text = $"{diskVal:F1}%";
            DiskUsageText.Foreground = GetUsageBrush(diskVal);
            DiskProgressBar.Value = Math.Clamp(diskVal, 0, 100);
            var read = data.GetValue(SensorType.DiskRead);
            var write = data.GetValue(SensorType.DiskWrite);
            DiskIoText.Text = read > 0 || write > 0
                ? $"R: {SensorFormatter.FormatBytesPerSec(read)} | W: {SensorFormatter.FormatBytesPerSec(write)}"
                : "--";

            var netDown = data.GetValue(SensorType.NetworkDown);
            var netUp = data.GetValue(SensorType.NetworkUp);
            NetworkDownText.Text = netDown > 0 ? "↓ " + SensorFormatter.FormatBytesPerSec(netDown) : "↓ --";
            NetworkUpText.Text = netUp > 0 ? "↑ " + SensorFormatter.FormatBytesPerSec(netUp) : "↑ --";

            var mbTemp = data.GetValue(SensorType.MotherboardTemp);
            MotherboardTempText.Text = mbTemp > 0 ? $"{mbTemp:F0}°C" : "--°C";
            MotherboardTempText.Foreground = PinBrush(GetTempColor(mbTemp));
            FanSpeedsText.Text = SensorFormatter.BuildFanSummary(data);

            GpuProcessList.ItemsSource = data.GpuProcesses;

            var uptime = DateTime.UtcNow - _startTime;
            UptimeText.Text = $"Tempo: {(int)uptime.TotalMinutes}m {uptime.Seconds}s";

            SensorCountText.Text = $"Sensores: {data.SensorDebugInfo.Count} detectados";
            if (data.CpuTemp <= 0)
                SensorCountText.Text += " | CPU temp: N/A";

            _debugWindow?.Update(data);

            LastUpdateText.Text = $"Atualizado às {data.Timestamp:HH:mm:ss}";

            _overlay?.Update(data);
            _alerts?.Evaluate(data);
            _csvLogger?.Write(data);
            _jsonLogger?.Write(data);
            _telemetrySdk?.Publish(data);
        });
    }

    private DebugWindow? _debugWindow;
    private OverlayEditorWindow? _overlayEditor;
    private InventoryWindow? _inventoryWindow;
    private TelemetrySdkWriter? _telemetrySdk;

    private void OnOpenDebugWindow(object sender, RoutedEventArgs e) => ShowDebugWindow();

    private void ShowDebugWindow()
    {
        if (_debugWindow == null || !_debugWindow.IsVisible)
        {
            _debugWindow = new DebugWindow();
            _debugWindow.Owner = this;
            _debugWindow.Closed += (_, _) => _debugWindow = null;
            _debugWindow.Show();
        }
        else
        {
            _debugWindow.Activate();
        }
    }

    private void OnOpenInventory(object sender, RoutedEventArgs e) => ShowInventoryWindow();

    private void ShowInventoryWindow()
    {
        if (_inventoryWindow == null || !_inventoryWindow.IsVisible)
        {
            _inventoryWindow = new InventoryWindow(() => _sensorService?.GetCurrentSnapshot(), ResetSeriesStats);
            _inventoryWindow.Owner = this;
            _inventoryWindow.Closed += (_, _) => _inventoryWindow = null;
            _inventoryWindow.Show();
        }
        else
        {
            _inventoryWindow.Activate();
        }
    }

    private void OnOpenOverlayEditor(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_overlayEditor == null || !_overlayEditor.IsVisible)
            {
                _overlayEditor = new OverlayEditorWindow(_config, ConfigPaths.ConfigFile);
                _overlayEditor.Owner = this;
                _overlayEditor.ConfigApplied += OnOverlayConfigApplied;
                _overlayEditor.Closed += (_, _) => _overlayEditor = null;
                _overlayEditor.Show();
            }
            else
            {
                _overlayEditor.Activate();
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("OpenOverlayEditor", ex.ToString());
            MessageBox.Show($"Não foi possível abrir o editor do overlay:\n\n{ex.Message}",
                "Phantom Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOverlayConfigApplied(AppConfig newConfig)
    {
        _config = newConfig;
        ApplyHotkey();
        ApplyAlertsConfig();

        // Só recria se o monitoramento está ativo; respeita o estado do toggle (visível/oculto).
        if (_sensorService == null) return;

        _overlay?.Close();
        _overlay = new OverlayWindow(_config, _config.OverlayPosition.X, _config.OverlayPosition.Y, ResolveProfileState);
        if (OverlayToggle.IsChecked == true)
            _overlay.Show();
    }

    private static readonly object TempBrushLock = new();
    private static readonly Dictionary<Color, SolidColorBrush> TempBrushCache = new();

    /// <summary>Escova congelada por cor, reutilizada nos ticks (criava SolidColorBrush a cada snapshot).</summary>
    private static Brush PinBrush(Color color)
    {
        lock (TempBrushLock)
        {
            if (!TempBrushCache.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush(color);
                brush.Freeze();
                TempBrushCache[color] = brush;
            }
            return brush;
        }
    }

    private static readonly Brush UsageNormalBrush = PinBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
    private static readonly Brush UsageWarmBrush = PinBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush UsageHotBrush = PinBrush(Color.FromRgb(0xEF, 0x44, 0x44));

    private static Brush GetUsageBrush(double percent) => percent switch
    {
        < 75 => UsageNormalBrush,
        < 90 => UsageWarmBrush,
        _ => UsageHotBrush
    };

    private static Color GetTempColor(double temp)
    {
        if (temp <= 0) return Color.FromRgb(0x64, 0x74, 0x8B);
        if (temp <= 65) return Color.FromRgb(0x10, 0xB9, 0x81);
        if (temp <= 80) return Color.FromRgb(0xF5, 0x9E, 0x0B);
        return Color.FromRgb(0xEF, 0x44, 0x44);
    }

    private static void UpdateTempRing(Border pill, TextBlock text, double temp)
    {
        var color = GetTempColor(temp);
        pill.BorderBrush = PinBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
        pill.Background = PinBrush(Color.FromArgb(0x18, color.R, color.G, color.B));
        text.Foreground = PinBrush(color);
        pill.Effect = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkeys.Dispose();
        _tray?.Dispose();
        if (_sensorService != null)
        {
            _sensorService.SensorUpdated -= OnSensorData;
            _sensorService.Dispose();
            _sensorService = null;
        }
        _csvLogger?.Dispose();
        _jsonLogger?.Dispose();
        _overlay?.Close();
        base.OnClosed(e);
    }
}
