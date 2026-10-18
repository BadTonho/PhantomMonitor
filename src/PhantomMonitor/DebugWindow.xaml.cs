using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PhantomMonitor.Models;

namespace PhantomMonitor;

public partial class DebugWindow : Window
{
    private bool _autoScroll = true;

    public DebugWindow()
    {
        InitializeComponent();
    }

    private void OnDebugScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeight <= e.ViewportHeight) { _autoScroll = true; return; }
        _autoScroll = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 5;
    }

    public void Update(SensorData data)
    {
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
        var order = new[] { "CPU", "GPU", "RAM", "DISCO", "REDE", "MOTHERBOARD", "FPS", "WMI", "PERF", "REG", "HWR", "SUB", "OUTROS" };
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
