using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
using RivaMonitor.Models;
using RivaMonitor.Overlay;
using RivaMonitor.Services;

namespace RivaMonitor;

public partial class MainWindow : Window
{
    private HardwareMonitorService? _sensorService;
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
        var cfg = AppConfig.Load(configPath);
        _config = cfg;

        _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDir);

        CsvToggle.IsChecked = cfg.LogToCsv;
        JsonToggle.IsChecked = cfg.LogToJson;

        if (!IsRunningAsAdmin())
            StatusText.Text = "Execute como Admin para sensores";

        StartMonitoring();
    }

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private void StartMonitoring()
    {
        OnStartClick(null!, null!);
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (_sensorService != null) return;

        _sensorService = new HardwareMonitorService(_config.UpdateIntervalMs);
        _sensorService.SensorUpdated += OnSensorData;
        _sensorService.Start();

        _overlay = new OverlayWindow(
            _config.OverlayPosition.X,
            _config.OverlayPosition.Y,
            _config.OverlayOpacity);

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
        StatusText.Text = "Monitorando...";
        StatusText.Foreground = FindResource("GreenBrush") as Brush;
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
    }

    private void OnOverlayToggled(object sender, RoutedEventArgs e)
    {
        if (_overlay == null) return;
        _overlay.Visibility = OverlayToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnSensorData(SensorData data)
    {
        Dispatcher.Invoke(() =>
        {
            CpuUsageText.Text = $"{data.CpuUsage:F1}%";
            CpuUsageText.Foreground = GetUsageBrush(data.CpuUsage);
            CpuRingText.Text = $"{data.CpuUsage:F0}%";
            CpuTempText.Text = data.CpuTemp > 0 ? $"{data.CpuTemp:F0}°C" : "--°C";

            GpuUsageText.Text = $"{data.GpuUsage:F1}%";
            GpuUsageText.Foreground = GetUsageBrush(data.GpuUsage);
            GpuRingText.Text = $"{data.GpuUsage:F0}%";
            GpuTempText.Text = data.GpuTemp > 0 ? $"{data.GpuTemp:F0}°C" : "--°C";

            RamUsageText.Text = $"{data.RamUsagePercent:F1}%";
            RamUsageText.Foreground = GetUsageBrush(data.RamUsagePercent);
            RamAvailableText.Text = data.RamAvailableGB > 0
                ? $"{data.RamAvailableGB:F1} GB disponível"
                : "-- GB disponível";

            DiskUsageText.Text = $"{data.GetValue(SensorType.DiskUsage):F1}%";
            DiskUsageText.Foreground = GetUsageBrush(data.GetValue(SensorType.DiskUsage));
            var read = data.GetValue(SensorType.DiskRead);
            var write = data.GetValue(SensorType.DiskWrite);
            DiskIoText.Text = read > 0 || write > 0
                ? $"R: {FormatBytes(read)}/s | W: {FormatBytes(write)}/s"
                : "--";

            GpuProcessList.ItemsSource = data.GpuProcesses;

            var uptime = DateTime.UtcNow - _startTime;
            UptimeText.Text = $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";

            SensorCountText.Text = $"Sensores: {data.SensorDebugInfo.Count} detectados";
            if (data.CpuTemp <= 0)
                SensorCountText.Text += " | CPU temp: N/A";

            LastUpdateText.Text = $"Última leitura: {data.Timestamp:HH:mm:ss}";

            _overlay?.Update(data);
            _csvLogger?.Write(data);
            _jsonLogger?.Write(data);
        });
    }

    private static Brush GetUsageBrush(double percent) => percent switch
    {
        < 50 => new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)),
        < 80 => new SolidColorBrush(Color.FromRgb(0xFF, 0xBB, 0x00)),
        _ => new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44))
    };

    private static string FormatBytes(double bytes)
    {
        if (bytes < 1024) return $"{bytes:F0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1} MB";
        return $"{bytes / (1024 * 1024 * 1024):F2} GB";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _sensorService?.Dispose();
        _csvLogger?.Dispose();
        _jsonLogger?.Dispose();
        _overlay?.Close();
        base.OnClosed(e);
    }
}
