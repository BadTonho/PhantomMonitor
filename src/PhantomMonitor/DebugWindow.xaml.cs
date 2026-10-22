using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor;

public partial class DebugWindow : Window
{
    private bool _autoScroll = true;
    private readonly ObservableCollection<ErrorLogEntry> _errorEntries = new();

    public DebugWindow()
    {
        InitializeComponent();
        ErrorEntriesList.ItemsSource = _errorEntries;
        ErrorLogPathText.Text = ErrorLog.LogFilePath;
        RefreshErrors();
        ErrorLog.EntryWritten += OnErrorLogEntryWritten;
        Closed += (_, _) => ErrorLog.EntryWritten -= OnErrorLogEntryWritten;
    }

    private void OnDebugScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeight <= e.ViewportHeight) { _autoScroll = true; return; }
        _autoScroll = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 5;
    }

    public void Update(SensorData data)
    {
        // Escondida ou minimizada: pular agrupamento + render do TextBox por tick.
        // Mesmo "aberta" atrás de outra janela não precisa de atualização contínua.
        if (!IsVisible || WindowState == WindowState.Minimized) return;

        Dispatcher.Invoke(() =>
        {
            var sv = FindScrollViewer(DebugTextBox);
            double scrollRatio = 0;
            bool wasAtBottom = _autoScroll;

            if (sv != null && sv.ExtentHeight > sv.ViewportHeight)
            {
                scrollRatio = sv.VerticalOffset / (sv.ExtentHeight - sv.ViewportHeight);
            }

            var grouped = GroupByCategory(data.SensorDebugInfo);
            var sb = new StringBuilder();

            foreach (var (category, lines) in grouped)
            {
                if (lines.Count == 0) continue;
                sb.AppendLine($"╔═══ {category} ({lines.Count})");
                sb.AppendLine($"║ {string.Join("\n║ ", lines)}");
                sb.AppendLine();
            }

            AppendAvailabilityDiagnostics(sb, data);

            DebugTextBox.Text = sb.ToString().TrimEnd();

            if (wasAtBottom)
            {
                DebugTextBox.ScrollToLine(DebugTextBox.LineCount - 1);
            }
            else if (sv != null)
            {
                // Restaurar posição após layout completar
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    var sv2 = FindScrollViewer(DebugTextBox);
                    if (sv2 != null && sv2.ExtentHeight > sv2.ViewportHeight)
                    {
                        var newOffset = scrollRatio * (sv2.ExtentHeight - sv2.ViewportHeight);
                        sv2.ScrollToVerticalOffset(newOffset);
                    }
                });
            }

            LastUpdateText.Text = $"Atualizado: {data.Timestamp:HH:mm:ss}  |  Total: {data.SensorDebugInfo.Count} sensores  |  Ctrl+A copiar";
        });
    }

    private static void AppendAvailabilityDiagnostics(StringBuilder output, SensorData data)
    {
        output.AppendLine("╔═══ DISPONIBILIDADE DE SÉRIES");
        var groups = new (string Name, SensorType[] Types)[]
        {
            ("CPU", [SensorType.CpuTotal, SensorType.CpuPerCore, SensorType.CpuTemperature, SensorType.CpuClock]),
            ("GPU", [SensorType.GpuCore, SensorType.GpuMemoryUsed, SensorType.GpuMemoryTotal,
                SensorType.GpuTemperature, SensorType.GpuHotSpot, SensorType.GpuVramTemperature]),
            ("MEMÓRIA", [SensorType.RamUsage, SensorType.RamUsed, SensorType.RamTotal, SensorType.RamAvailable]),
            ("DISCO", [SensorType.DiskRead, SensorType.DiskWrite, SensorType.DiskUsage]),
            ("REDE", [SensorType.NetworkDown, SensorType.NetworkUp]),
            ("VENTOINHAS", [SensorType.FanSpeed, SensorType.GpuFan]),
            ("BATERIA", [SensorType.BatteryLevel, SensorType.BatteryVoltage, SensorType.BatteryPower]),
            ("FPS", [SensorType.Fps, SensorType.FrametimeMs])
        };

        foreach (var (group, types) in groups)
        foreach (var type in types)
        {
            var present = data.Values.ContainsKey(type) || data.Series.Keys.Any(key => key.Type == type);
            output.AppendLine($"║ [{group}] {type}: {(present ? "disponível" : "sem leitura do provedor atual; consulte os detalhes da fonte acima")}");
        }

        output.AppendLine();
    }

    private void OnErrorLogEntryWritten(ErrorLogEntry entry)
    {
        if (!IsVisible) return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (!IsVisible) return;
                if (_errorEntries.Contains(entry)) return;
                _errorEntries.Add(entry);
                while (_errorEntries.Count > 500)
                    _errorEntries.RemoveAt(0);
                UpdateErrorLogStatus();
            }));
        }
        catch (InvalidOperationException)
        {
            // A janela está fechando.
        }
    }

    private void OnRefreshErrors(object sender, RoutedEventArgs e) => RefreshErrors();

    private void RefreshErrors()
    {
        _errorEntries.Clear();
        foreach (var entry in ErrorLog.ReadRecentEntries())
            _errorEntries.Add(entry);
        UpdateErrorLogStatus();
    }

    private void UpdateErrorLogStatus() => ErrorLogStatusText.Text = _errorEntries.Count == 0
        ? "Nenhum erro registrado no arquivo local. As novas ocorrências aparecem automaticamente."
        : $"Exibindo {_errorEntries.Count} registros recentes; novas ocorrências aparecem automaticamente.";

    private void OnOpenErrorLog(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorLog.EnsureLogFileExists();
            Process.Start(new ProcessStartInfo(ErrorLog.LogFilePath) { UseShellExecute = true });
            ErrorLogStatusText.Text = "Arquivo aberto no aplicativo padrão.";
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Diagnostics.OpenErrorLog", ex);
            ErrorLogStatusText.Text = $"Não foi possível abrir o arquivo: {ex.Message}";
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? obj)
    {
        if (obj == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    private static List<(string Category, List<string> Lines)> GroupByCategory(List<string> debugInfo)
    {
        var map = new Dictionary<string, List<string>>();

        foreach (var line in debugInfo)
        {
            var category = ParseCategory(line);
            if (!map.ContainsKey(category))
                map[category] = new List<string>();
            map[category].Add(line);
        }

        // Order by priority
        var order = new[] { "CPU", "GPU", "RAM", "DISCO", "REDE", "MOTHERBOARD", "FAN", "FPS", "WMI", "PERF", "REG", "HWR", "SUB", "OUTROS" };
        return order.Where(o => map.ContainsKey(o)).Select(o => (o, map[o])).ToList();
    }

    private static string ParseCategory(string line)
    {
        if (line.StartsWith("[CPU]")) return "CPU";
        if (line.StartsWith("[GPU]")) return "GPU";
        if (line.StartsWith("[RAM]")) return "RAM";
        if (line.StartsWith("[DISCO]") || line.StartsWith("[DISK]")) return "DISCO";
        if (line.StartsWith("[NET]") || line.StartsWith("[REDE]")) return "REDE";
        if (line.StartsWith("[FPS]")) return "FPS";
        if (line.StartsWith("[WMI]")) return "WMI";
        if (line.StartsWith("[PERF]")) return "PERF";
        if (line.StartsWith("[REG]")) return "REG";
        if (line.StartsWith("[HWR]")) return "HWR";
        if (line.StartsWith("[SUB]")) return "SUB";
        if (line.StartsWith("[MOTHER") || line.StartsWith("[MB]")) return "MOTHERBOARD";
        if (line.StartsWith("[FAN]")) return "FAN";
        return "OUTROS";
    }
}
