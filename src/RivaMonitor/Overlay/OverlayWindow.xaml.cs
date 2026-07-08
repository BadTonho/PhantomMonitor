using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RivaMonitor.Models;
using RivaMonitor.Services;

namespace RivaMonitor.Overlay;

public partial class OverlayWindow : Window
{
    private readonly List<SensorRow> _rows = new();
    private Canvas? _frametimeGraph;
    private bool _showFps;

    private sealed class SensorRow
    {
        public TextBlock ValueText { get; }
        public Border BarFill { get; }
        public Func<SensorData, double>? GetValue { get; }
        public Func<SensorData, string>? GetText { get; }
        public double MaxValue { get; }

        public SensorRow(TextBlock valueText, Border barFill,
            Func<SensorData, double>? getValue, Func<SensorData, string>? getText,
            double maxValue)
        {
            ValueText = valueText;
            BarFill = barFill;
            GetValue = getValue;
            GetText = getText;
            MaxValue = maxValue;
        }
    }

    public OverlayWindow(AppConfig config, double left = 20, double top = 80)
    {
        InitializeComponent();
        Left = left;
        Top = top;
        Width = config.Overlay.Width;
        Opacity = config.OverlayOpacity;

        if (!string.IsNullOrEmpty(config.Overlay.AccentColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(config.Overlay.AccentColor);
                MainBorder.BorderBrush = new SolidColorBrush(color);
            }
            catch { }
        }

        MakeClickThrough();

        if (config.Overlay.ShowCpu)
            AddSection("CPU", MakeEntries(
                ("Uso",  GetCpuUsage, GetCpuUsageText, 100.0),
                ("Temp", null, GetCpuTempText, 100.0)
            ));

        if (config.Overlay.ShowGpu)
            AddSection("GPU", MakeEntries(
                ("Uso",  GetGpuUsage, GetGpuUsageText, 100.0),
                ("Temp", null, GetGpuTempText, 100.0)
            ));

        if (config.Overlay.ShowRam)
            AddSection("RAM", MakeEntries(
                ("Uso", GetRamUsage, GetRamUsageText, 100.0)
            ));

        if (config.Overlay.ShowDisk)
            AddSection("DISCO", MakeEntries(
                ("Leitura",  null, GetDiskReadText, 100.0),
                ("Escrita", null, GetDiskWriteText, 100.0)
            ));

        _showFps = config.Overlay.ShowFps;
        if (_showFps)
            AddFpsSection();

        _offsetX = left;
        _offsetY = top;
        var timer = new System.Windows.Threading.DispatcherTimer();
        timer.Interval = TimeSpan.FromMilliseconds(30);
        timer.Tick += (s, e) => UpdatePosition();
        timer.Start();
    }

    private double _offsetX;
    private double _offsetY;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private void UpdatePosition()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == System.Diagnostics.Process.GetCurrentProcess().Id) return; // Ignora se for a própria janela do RivaMonitor (MainWindow/DebugWindow)
        
        // Ignorar o desktop/explorer
        if (pid == GetExplorerPid()) return;

        if (GetWindowRect(hwnd, out var rect))
        {
            // Verifica se não está minimizada
            if (rect.Left <= -32000) return;

            this.Left = rect.Left + _offsetX;
            this.Top = rect.Top + _offsetY;
        }
    }

    private static uint _explorerPid = 0;
    private static uint GetExplorerPid()
    {
        if (_explorerPid != 0) return _explorerPid;
        var procs = System.Diagnostics.Process.GetProcessesByName("explorer");
        if (procs.Length > 0) _explorerPid = (uint)procs[0].Id;
        return _explorerPid;
    }

    private static (string label, Func<SensorData, double>? getValue, Func<SensorData, string>? getText, double max)[] MakeEntries(
        params (string label, Func<SensorData, double>? getValue, Func<SensorData, string>? getText, double max)[] entries) => entries;

    private void AddSection(string title, (string label, Func<SensorData, double>? getValue, Func<SensorData, string>? getText, double max)[] entries)
    {
        var section = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(10, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var stack = new StackPanel();
        var accent = MainBorder.BorderBrush;

        var header = new TextBlock
        {
            Text = title,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 0, 4)
        };
        stack.Children.Add(header);

        foreach (var (label, getValue, getText, max) in entries)
        {
            var row = BuildRow(label, getValue, getText, max);
            stack.Children.Add(row.Item1);
            _rows.Add(row.Item2);
        }

        section.Child = stack;
        SensorPanel.Children.Add(section);
    }

    private (UIElement, SensorRow) BuildRow(string label,
        Func<SensorData, double>? getValue,
        Func<SensorData, string>? getText,
        double maxValue)
    {
        var outerGrid = new Grid();
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Label + Value row
        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(lbl, 0);
        topGrid.Children.Add(lbl);

        var val = new TextBlock
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "--"
        };
        Grid.SetColumn(val, 1);
        topGrid.Children.Add(val);
        Grid.SetRow(topGrid, 0);
        outerGrid.Children.Add(topGrid);

        // Bar
        var barGrid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        var barBg = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(Color.FromArgb(30, 0xFF, 0xFF, 0xFF))
        };
        barGrid.Children.Add(barBg);

        var barFill = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF))
        };
        barGrid.Children.Add(barFill);
        Grid.SetRow(barGrid, 1);
        outerGrid.Children.Add(barGrid);

        var row = new SensorRow(val, barFill, getValue, getText, maxValue);
        return (outerGrid, row);
    }

    private void AddFpsSection()
    {
        var section = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(10, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var stack = new StackPanel();
        var accent = MainBorder.BorderBrush;

        var header = new TextBlock
        {
            Text = "FPS",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 0, 4)
        };
        stack.Children.Add(header);

        // FPS + Frametime row
        var fpsGrid = new Grid();
        fpsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fpsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fpsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fpsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fpsLabel = new TextBlock
        {
            Text = "FPS",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(fpsLabel, 0);
        fpsGrid.Children.Add(fpsLabel);

        var fpsVal = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "--",
            Margin = new Thickness(6, 0, 12, 0)
        };
        Grid.SetColumn(fpsVal, 1);
        fpsGrid.Children.Add(fpsVal);
        _rows.Add(new SensorRow(fpsVal, null!, d => d.Fps, null, 240));

        var ftLabel = new TextBlock
        {
            Text = "ms",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(ftLabel, 2);
        fpsGrid.Children.Add(ftLabel);

        var ftVal = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xBB, 0x00)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "--",
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(ftVal, 3);
        fpsGrid.Children.Add(ftVal);
        _rows.Add(new SensorRow(ftVal, null!, null, d => d.FrametimeMs > 0 ? $"{d.FrametimeMs:F1}" : "--", 0));

        stack.Children.Add(fpsGrid);

        // Stats row: avg / min / 1% low
        var statsGrid = new Grid();
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.Margin = new Thickness(0, 4, 0, 0);

        AddStat(statsGrid, 0, "Avg", "FpsAvg", d => d.FpsAvg > 0 ? $"{d.FpsAvg:F0}" : "--");
        AddStat(statsGrid, 1, "Min", "FpsMin", d => d.FpsMin > 0 ? $"{d.FpsMin:F0}" : "--");
        AddStat(statsGrid, 2, "1% Low", "Fps1Low", d => d.Fps1Low > 0 ? $"{d.Fps1Low:F0}" : "--");

        stack.Children.Add(statsGrid);

        // Frametime graph
        _frametimeGraph = new Canvas
        {
            Height = 40,
            Margin = new Thickness(0, 4, 0, 0),
            ClipToBounds = true
        };
        stack.Children.Add(_frametimeGraph);

        section.Child = stack;
        SensorPanel.Children.Add(section);
    }

    private static void AddStat(Grid parent, int col, string label, string _, Func<SensorData, string> getter)
    {
        var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 8,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        inner.Children.Add(lbl);

        var val = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = "--"
        };
        inner.Children.Add(val);
        Grid.SetColumn(inner, col);
        parent.Children.Add(inner);
    }

    public void Update(SensorData data)
    {
        foreach (var row in _rows)
        {
            if (row.GetValue != null)
            {
                var val = row.GetValue(data);
                var pct = Math.Clamp(val / row.MaxValue, 0, 1);
                row.BarFill.Width = pct * (ActualWidth - 56);

                if (pct < 0.5)
                    row.BarFill.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));
                else if (pct < 0.8)
                    row.BarFill.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xBB, 0x00));
                else
                    row.BarFill.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
            }

            if (row.GetText != null)
                row.ValueText.Text = row.GetText(data);
        }

        // Update frametime graph
        if (_frametimeGraph != null && data.FrametimeHistory.Length > 1)
        {
            _frametimeGraph.Children.Clear();
            var w = _frametimeGraph.ActualWidth;
            var h = _frametimeGraph.ActualHeight;
            if (w <= 0 || h <= 0) { w = _frametimeGraph.Width; h = _frametimeGraph.Height; }
            if (w <= 0) w = Width - 36;
            if (h <= 0) h = 40;

            var history = data.FrametimeHistory;
            var maxFt = 33.0; // ~30fps worst case
            for (int i = 0; i < history.Length; i++)
                if (history[i] > maxFt) maxFt = history[i];
            if (maxFt < 8) maxFt = 16;

            var pts = new PointCollection();
            for (int i = 0; i < history.Length; i++)
            {
                var x = w - (history.Length - i) * (w / Math.Max(history.Length, 300));
                var y = h - (history[i] / maxFt) * (h - 2);
                pts.Add(new Point(x, Math.Clamp(y, 0, h)));
            }

            var poly = new Polyline
            {
                Points = pts,
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xBB, 0x00)),
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round
            };
            _frametimeGraph.Children.Add(poly);
        }
    }

    // Getters
    private static double GetCpuUsage(SensorData d) => d.CpuUsage;
    private static string GetCpuUsageText(SensorData d) => $"{d.CpuUsage:F1}%";
    private static string GetCpuTempText(SensorData d) => d.CpuTemp > 0 ? $"{d.CpuTemp:F0}°C" : "--°C";

    private static double GetGpuUsage(SensorData d) => d.GpuUsage;
    private static string GetGpuUsageText(SensorData d) => $"{d.GpuUsage:F1}%";
    private static string GetGpuTempText(SensorData d) => d.GpuTemp > 0 ? $"{d.GpuTemp:F0}°C" : "--°C";

    private static double GetRamUsage(SensorData d) => d.RamUsagePercent;
    private static string GetRamUsageText(SensorData d) => $"{d.RamUsagePercent:F1}%";

    private static string GetDiskReadText(SensorData d) => FormatBytes(d.GetValue(SensorType.DiskRead)) + "/s";
    private static string GetDiskWriteText(SensorData d) => FormatBytes(d.GetValue(SensorType.DiskWrite)) + "/s";

    private static string FormatBytes(double bytes)
    {
        if (bytes < 1024) return $"{bytes:F0}B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1}KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1}MB";
        return $"{bytes / (1024 * 1024 * 1024):F2}GB";
    }

    private void MakeClickThrough()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            SourceInitialized += (_, _) => SetExTransparent();
        else
            SetExTransparent();
    }

    private void SetExTransparent()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
