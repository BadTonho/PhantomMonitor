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
        StartTopmostTimer();

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
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(12, 0x00, 0xF0, 0xFF)), // Vidro ciano translúcido
            BorderBrush = FindResource("DividerBrush") as Brush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var stack = new StackPanel();

        var header = new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("AccentGradientBrush") as Brush, // Cabeçalhos com gradiente neon
            Margin = new Thickness(0, 0, 0, 6)
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
        var topGrid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = FindResource("TextSecondaryBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(lbl, 0);
        topGrid.Children.Add(lbl);

        var val = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "--"
        };
        Grid.SetColumn(val, 1);
        topGrid.Children.Add(val);
        Grid.SetRow(topGrid, 0);
        outerGrid.Children.Add(topGrid);

        // Bar
        var barGrid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        var barBg = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromArgb(20, 0xFF, 0xFF, 0xFF))
        };
        barGrid.Children.Add(barBg);

        var barFill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
            Background = FindResource("AccentGradientBrush") as Brush
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
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(12, 0x00, 0xF0, 0xFF)), // Vidro ciano translúcido
            BorderBrush = FindResource("DividerBrush") as Brush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var stack = new StackPanel();

        var header = new TextBlock
        {
            Text = "FPS",
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("AccentGradientBrush") as Brush,
            Margin = new Thickness(0, 0, 0, 6)
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
            Foreground = FindResource("TextSecondaryBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(fpsLabel, 0);
        fpsGrid.Children.Add(fpsLabel);

        var fpsVal = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("GreenBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "--",
            Margin = new Thickness(6, 0, 12, 0)
        };
        Grid.SetColumn(fpsVal, 1);
        fpsGrid.Children.Add(fpsVal);
        _rows.Add(new SensorRow(fpsVal, null!, null, d => d.Fps > 0 ? $"{d.Fps:F0}" : d.Fps == 0 ? "0" : "--", 240));

        var ftLabel = new TextBlock
        {
            Text = "ms",
            FontSize = 10,
            Foreground = FindResource("TextSecondaryBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(ftLabel, 2);
        fpsGrid.Children.Add(ftLabel);

        var ftVal = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("YellowBrush") as Brush,
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
        statsGrid.Margin = new Thickness(0, 6, 0, 2);

        AddStat(statsGrid, 0, "Avg", "FpsAvg", d => d.FpsAvg > 0 ? $"{d.FpsAvg:F0}" : "--");
        AddStat(statsGrid, 1, "Min", "FpsMin", d => d.FpsMin > 0 ? $"{d.FpsMin:F0}" : "--");
        AddStat(statsGrid, 2, "1% Low", "Fps1Low", d => d.Fps1Low > 0 ? $"{d.Fps1Low:F0}" : "--");

        stack.Children.Add(statsGrid);

        // Frametime graph
        _frametimeGraph = new Canvas
        {
            Height = 40,
            Margin = new Thickness(0, 6, 0, 0),
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
            Foreground = parent.FindResource("TextSecondaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        inner.Children.Add(lbl);

        var val = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = parent.FindResource("TextPrimaryBrush") as Brush,
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
                    row.BarFill.Background = FindResource("GreenBrush") as Brush;
                else if (pct < 0.8)
                    row.BarFill.Background = FindResource("YellowBrush") as Brush;
                else
                    row.BarFill.Background = FindResource("RedBrush") as Brush;
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
                Stroke = FindResource("YellowBrush") as Brush,
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.6,
                    Color = (Color)FindResource("Yellow")
                }
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
        SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void StartTopmostTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.Tick += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };
        timer.Start();
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
