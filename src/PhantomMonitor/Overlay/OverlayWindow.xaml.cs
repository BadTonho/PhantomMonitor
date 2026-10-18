using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.Overlay;

public partial class OverlayWindow : Window
{
    private readonly List<Action<SensorData>> _updaters = new();
    private Canvas? _frametimeGraph;
    private bool _showFps;

    private static readonly FontFamily OsdFont = new("Consolas, Cascadia Mono, Lucida Console, Segoe UI");
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
    private static readonly Brush WarmBrush = new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15)); // Amarelo
    private static readonly Brush HotBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));  // Vermelho
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0x9A, 0xA8));
    private static readonly Brush ColonBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
    private static readonly Brush GreenFpsBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));

    public OverlayWindow(AppConfig config, double left = 20, double top = 80)
    {
        InitializeComponent();
        Left = left;
        Top = top;
        Opacity = config.OverlayOpacity;
        if (config.Overlay.Width > 0)
            MainBorder.MinWidth = config.Overlay.Width;

        MakeClickThrough();
        StartTopmostTimer();

        // GPU
        if (config.Overlay.ShowGpu)
        {
            AddMetricRow("GPU", Color.FromRgb(0xFF, 0x8A, 0x00), data =>
            {
                string tempStr = data.GpuTemp > 0 ? $"{data.GpuTemp:F0}°C" : "--°C";
                string usageStr = $"{data.GpuUsage:F1}%";
                return (tempStr, GetTempBrush(data.GpuTemp), usageStr, GetUsageBrush(data.GpuUsage));
            });
        }

        // CPU
        if (config.Overlay.ShowCpu)
        {
            AddMetricRow("CPU", Color.FromRgb(0x00, 0xD2, 0xFF), data =>
            {
                string tempStr = data.CpuTemp > 0 ? $"{data.CpuTemp:F0}°C" : "--°C";
                string usageStr = $"{data.CpuUsage:F1}%";
                return (tempStr, GetTempBrush(data.CpuTemp), usageStr, GetUsageBrush(data.CpuUsage));
            });
        }

        // RAM
        if (config.Overlay.ShowRam)
        {
            AddMetricRow("RAM", Color.FromRgb(0xC0, 0x84, 0xFC), data =>
            {
                double total = data.GetValue(SensorType.RamTotal);
                double avail = data.RamAvailableGB;
                string usedStr;
                if (total > 0 && avail > 0 && total >= avail)
                {
                    double used = total - avail;
                    usedStr = $"{used:F1} GB";
                }
                else
                {
                    usedStr = $"{data.RamUsagePercent:F0}%";
                }

                string pctStr = $"{data.RamUsagePercent:F0}%";
                return (usedStr, NormalBrush, pctStr, GetUsageBrush(data.RamUsagePercent));
            });
        }

        // DISCO
        if (config.Overlay.ShowDisk)
        {
            AddMetricRow("DSK", Color.FromRgb(0x60, 0xA5, 0xFA), data =>
            {
                string readStr = "R: " + FormatBytes(data.GetValue(SensorType.DiskRead));
                string writeStr = "W: " + FormatBytes(data.GetValue(SensorType.DiskWrite));
                return (readStr, MutedBrush, writeStr, MutedBrush);
            });
        }

        // FPS
        _showFps = config.Overlay.ShowFps;
        if (_showFps)
        {
            AddFpsSection(config.Overlay.ShowFrametimeGraph);
        }

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
        if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return;

        if (pid == GetExplorerPid()) return;

        if (GetWindowRect(hwnd, out var rect))
        {
            if (rect.Left <= -32000) return;

            Left = rect.Left + _offsetX;
            Top = rect.Top + _offsetY;
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

    private void AddMetricRow(
        string tag,
        Color tagColor,
        Func<SensorData, (string val1, Brush col1, string val2, Brush col2)> dataProvider)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 1.5, 0, 1.5)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); // Tag
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });  // :
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); // Métrica 1
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Métrica 2

        var tagBlock = new TextBlock
        {
            Text = tag,
            FontFamily = OsdFont,
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(tagColor),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tagBlock, 0);
        grid.Children.Add(tagBlock);

        var colonBlock = new TextBlock
        {
            Text = ":",
            FontFamily = OsdFont,
            FontSize = 11.5,
            Foreground = ColonBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(colonBlock, 1);
        grid.Children.Add(colonBlock);

        var val1Block = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = NormalBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = "--"
        };
        Grid.SetColumn(val1Block, 2);
        grid.Children.Add(val1Block);

        var val2Block = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = NormalBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = "--"
        };
        Grid.SetColumn(val2Block, 3);
        grid.Children.Add(val2Block);

        _updaters.Add(data =>
        {
            var (v1, c1, v2, c2) = dataProvider(data);
            val1Block.Text = v1;
            val1Block.Foreground = c1 ?? NormalBrush;
            val2Block.Text = v2;
            val2Block.Foreground = c2 ?? NormalBrush;
        });

        SensorPanel.Children.Add(grid);
    }

    private void AddFpsSection(bool showFrametimeGraph = true)
    {
        // Linha Principal de FPS
        var grid = new Grid
        {
            Margin = new Thickness(0, 1.5, 0, 1)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); // FPS
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });  // :
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); // FPS valor
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Frametime ms

        var tagBlock = new TextBlock
        {
            Text = "FPS",
            FontFamily = OsdFont,
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = GreenFpsBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tagBlock, 0);
        grid.Children.Add(tagBlock);

        var colonBlock = new TextBlock
        {
            Text = ":",
            FontFamily = OsdFont,
            FontSize = 11.5,
            Foreground = ColonBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(colonBlock, 1);
        grid.Children.Add(colonBlock);

        var fpsBlock = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = GreenFpsBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = "-- FPS"
        };
        Grid.SetColumn(fpsBlock, 2);
        grid.Children.Add(fpsBlock);

        var ftBlock = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = WarmBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = "-- ms"
        };
        Grid.SetColumn(ftBlock, 3);
        grid.Children.Add(ftBlock);

        SensorPanel.Children.Add(grid);

        // Sublinha: AVG / 1% Low
        var statsGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 2)
        };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) }); // Espaço sob a tag
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var statsBlock = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = 9.5,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = "AVG: --  1%: --"
        };
        Grid.SetColumn(statsBlock, 1);
        statsGrid.Children.Add(statsBlock);

        SensorPanel.Children.Add(statsGrid);

        if (showFrametimeGraph)
        {
            // Gráfico de Frametime compacto estilo RivaTuner
            _frametimeGraph = new Canvas
            {
                Height = 24,
                Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)),
                ClipToBounds = true
            };

            var graphBorder = new Border
            {
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(0.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Child = _frametimeGraph,
                Margin = new Thickness(0, 2, 0, 0)
            };

            SensorPanel.Children.Add(graphBorder);
        }

        _updaters.Add(data =>
        {
            fpsBlock.Text = data.Fps > 0 ? $"{data.Fps:F0} FPS" : "-- FPS";
            ftBlock.Text = data.FrametimeMs > 0 ? $"{data.FrametimeMs:F1} ms" : "-- ms";

            string avg = data.FpsAvg > 0 ? $"{data.FpsAvg:F0}" : "--";
            string low = data.Fps1Low > 0 ? $"{data.Fps1Low:F0}" : "--";
            statsBlock.Text = $"AVG: {avg}  1%: {low}";
        });
    }

    public void Update(SensorData data)
    {
        foreach (var updater in _updaters)
        {
            updater(data);
        }

        // Atualizar o gráfico de frametime
        if (_frametimeGraph != null && data.FrametimeHistory.Length > 1)
        {
            _frametimeGraph.Children.Clear();
            double w = _frametimeGraph.ActualWidth;
            double h = _frametimeGraph.ActualHeight;
            if (w <= 0 || h <= 0) { w = _frametimeGraph.Width; h = _frametimeGraph.Height; }
            if (w <= 0) w = 184;
            if (h <= 0) h = 24;

            var history = data.FrametimeHistory;
            double maxFt = 33.3; // 30 FPS baseline
            for (int i = 0; i < history.Length; i++)
            {
                if (history[i] > maxFt) maxFt = history[i];
            }
            if (maxFt < 16.6) maxFt = 16.6;

            // Linha guia sutil de 60 FPS (16.6ms)
            double y60 = h - (16.66 / maxFt) * (h - 2);
            if (y60 >= 0 && y60 <= h)
            {
                var guide = new Line
                {
                    X1 = 0,
                    X2 = w,
                    Y1 = y60,
                    Y2 = y60,
                    Stroke = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    StrokeThickness = 0.8,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                _frametimeGraph.Children.Add(guide);
            }

            var pts = new PointCollection();
            int count = history.Length;
            for (int i = 0; i < count; i++)
            {
                double x = w - (count - i) * (w / Math.Max(count, 120));
                double y = h - (history[i] / maxFt) * (h - 2);
                pts.Add(new Point(x, Math.Clamp(y, 0, h)));
            }

            var poly = new Polyline
            {
                Points = pts,
                Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF)),
                StrokeThickness = 1.2,
                StrokeLineJoin = PenLineJoin.Round
            };
            _frametimeGraph.Children.Add(poly);
        }
    }

    private static Brush GetTempBrush(double temp)
    {
        if (temp >= 80) return HotBrush;
        if (temp >= 70) return WarmBrush;
        return NormalBrush;
    }

    private static Brush GetUsageBrush(double usage)
    {
        if (usage >= 90) return HotBrush;
        if (usage >= 75) return WarmBrush;
        return NormalBrush;
    }

    private static string FormatBytes(double bytes)
    {
        if (bytes < 1024) return $"{bytes:F0}B/s";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F0}KB/s";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1}MB/s";
        return $"{bytes / (1024 * 1024 * 1024):F2}GB/s";
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
    private static readonly IntPtr HWND_TOPMOST = new(-1);
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
