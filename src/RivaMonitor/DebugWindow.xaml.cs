using System.Text;
using System.Windows;
using RivaMonitor.Models;

namespace RivaMonitor;

public partial class DebugWindow : Window
{
    public DebugWindow()
    {
        InitializeComponent();
    }

    public void Update(SensorData data)
    {
        Dispatcher.Invoke(() =>
        {
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
            LastUpdateText.Text = $"Atualizado: {data.Timestamp:HH:mm:ss}  |  Total: {data.SensorDebugInfo.Count} sensores  |  Ctrl+A copiar";
        });
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
