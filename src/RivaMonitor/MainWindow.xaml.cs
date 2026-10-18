using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RivaMonitor.Models;
using RivaMonitor.Overlay;
using RivaMonitor.Services;

namespace RivaMonitor;

public partial class MainWindow : Window
{
    private ISensorService? _sensorService;
    private OverlayWindow? _overlay;
    private CsvLogger? _csvLogger;
    private JsonLogger? _jsonLogger;
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
        var configPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "config.json");
        _config = AppConfig.Load(configPath);

        _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDir);

        CsvToggle.IsChecked = _config.LogToCsv;
        JsonToggle.IsChecked = _config.LogToJson;

        if (!IsRunningAsAdmin())
        {
            AdminWarning.Visibility = Visibility.Visible;
            StatusText.Text = "Clique em 'Executar como Admin'";
        }

        StartMonitoring();
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
        catch
        {
            MessageBox.Show("Não foi possível elevar o aplicativo. Execute manualmente como Administrador.",
                "Riva Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StartMonitoring()
    {
        OnStartClick(null!, null!);
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (_sensorService != null) return;

        _sensorService = SensorServiceFactory.Create(_config.UpdateIntervalMs);
        _sensorService.SensorUpdated += OnSensorData;
        _sensorService.Start();

        _overlay = new OverlayWindow(_config,
            _config.OverlayPosition.X,
            _config.OverlayPosition.Y);

        if (OverlayToggle.IsChecked == true)
            _overlay.Show();

        if (CsvToggle.IsChecked == true)
        {
            var path = Path.Combine(_logDir, $"sensors_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            _csvLogger = new CsvLogger(path, Enum.GetValues<SensorType>());
        }

        if (JsonToggle.IsChecked == true)
        {
            var path = Path.Combine(_logDir, "sensors_latest.json");
            _jsonLogger = new JsonLogger(path);
        }

        _startTime = DateTime.UtcNow;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "Monitorando";
        StatusText.Foreground = FindResource("GreenBrush") as Brush;
        StatusDot.Fill = FindResource("GreenBrush") as Brush;
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        _sensorService?.Stop();
        _overlay?.Close();
        _csvLogger?.Dispose();
        _jsonLogger?.Dispose();

        _sensorService = null;
        _overlay = null;
        _csvLogger = null;
        _jsonLogger = null;

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "Parado";
        StatusText.Foreground = FindResource("TextSecondaryBrush") as Brush;
        StatusDot.Fill = FindResource("TextMutedBrush") as Brush;
    }

    private void OnOverlayToggled(object sender, RoutedEventArgs e)
    {
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

    private void OnSensorData(SensorData data)
    {
        Dispatcher.Invoke(() =>
        {
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

            FpsText.Text = data.Fps > 0 ? $"{data.Fps:F0}" : "--";
            FpsDetailsText.Text = data.Fps > 0
                ? $"Mín: {data.FpsMin:F0} | Avg: {data.FpsAvg:F0} | 1% Low: {data.Fps1Low:F0}"
                : "Mín: -- | Avg: -- | 1% Low: --";
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
                ? $"R: {FormatBytes(read)}/s | W: {FormatBytes(write)}/s"
                : "--";

            GpuProcessList.ItemsSource = data.GpuProcesses;

            var uptime = DateTime.UtcNow - _startTime;
            UptimeText.Text = $"Tempo: {(int)uptime.TotalMinutes}m {uptime.Seconds}s";

            SensorCountText.Text = $"Sensores: {data.SensorDebugInfo.Count} detectados";
            if (data.CpuTemp <= 0)
                SensorCountText.Text += " | CPU temp: N/A";

            _debugWindow?.Update(data);

            LastUpdateText.Text = $"Atualizado às {data.Timestamp:HH:mm:ss}";

            _overlay?.Update(data);
            _csvLogger?.Write(data);
            _jsonLogger?.Write(data);
        });
    }

    private DebugWindow? _debugWindow;
    private OverlayEditorWindow? _overlayEditor;

    public void OpenDebugWindow() => OnOpenDebugWindow(null!, null!);

    private void OnOpenDebugWindow(object sender, RoutedEventArgs e)
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

    private void OnOpenOverlayEditor(object sender, RoutedEventArgs e)
    {
        try
        {
            var configPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "config.json");

            if (_overlayEditor == null || !_overlayEditor.IsVisible)
            {
                _overlayEditor = new OverlayEditorWindow(_config, configPath);
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
            MessageBox.Show($"Não foi possível abrir o editor do overlay:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Riva Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOverlayConfigApplied(AppConfig newConfig)
    {
        _config = newConfig;

        if (_overlay != null && _overlay.IsVisible)
        {
            _overlay.Close();
            _overlay = new OverlayWindow(_config, _config.OverlayPosition.X, _config.OverlayPosition.Y);
            _overlay.Show();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static Brush GetUsageBrush(double percent) => percent switch
    {
        < 75 => new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
        < 90 => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        _ => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
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
        pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
        pill.Background = new SolidColorBrush(Color.FromArgb(0x18, color.R, color.G, color.B));
        text.Foreground = new SolidColorBrush(color);
        pill.Effect = null;
    }

    private static string FormatBytes(double bytes)
    {
        if (bytes < 1024) return $"{bytes:F0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1} MB";
        return $"{bytes / (1024 * 1024 * 1024):F2} GB";
    }

    protected override void OnClosed(EventArgs e)
    {
        _sensorService?.Dispose();
        _csvLogger?.Dispose();
        _jsonLogger?.Dispose();
        _overlay?.Close();
        base.OnClosed(e);
    }
}
