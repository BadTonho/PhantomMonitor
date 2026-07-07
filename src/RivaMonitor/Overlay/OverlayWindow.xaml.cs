using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RivaMonitor.Models;

namespace RivaMonitor.Overlay;

public partial class OverlayWindow : Window
{
    public OverlayWindow(double left = 20, double top = 80, double opacity = 0.85)
    {
        InitializeComponent();
        Left = left;
        Top = top;
        Opacity = opacity;
        MakeClickThrough();
    }

    public void Update(SensorData data)
    {
        var items = new List<OverlayItem>
        {
            new("CPU", $"{data.CpuUsage:F1}%", data.CpuUsage / 100.0),
            new("CPU Temp", $"{data.CpuTemp:F0}°C", data.CpuTemp / 100.0),
        };

        if (data.GetValue(SensorType.GpuCore) > 0 || data.GetValue(SensorType.GpuTemperature) > 0)
        {
            items.Add(new("GPU", $"{data.GpuUsage:F1}%", data.GpuUsage / 100.0));
            items.Add(new("GPU Temp", $"{data.GpuTemp:F0}°C", data.GpuTemp / 100.0));
        }

        if (data.GetValue(SensorType.GpuCoreClock) > 0)
            items.Add(new("GPU Clock", $"{data.GetValue(SensorType.GpuCoreClock):F0}MHz",
                         data.GetValue(SensorType.GpuCoreClock) / 3000.0));

        items.Add(new("RAM", $"{data.RamUsagePercent:F1}%", data.RamUsagePercent / 100.0));

        if (data.GetValue(SensorType.FanSpeed) > 0)
            items.Add(new("Fan", $"{data.GetValue(SensorType.FanSpeed):F0}RPM", 0));

        SensorList.ItemsSource = items;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MakeClickThrough()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            SourceInitialized += (_, _) => SetExTransparent();
        }
        else
        {
            SetExTransparent();
        }
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

public sealed class OverlayItem : INotifyPropertyChanged
{
    private string _label;
    private string _valueText;
    private double _barWidth;
    private Brush _barColor;

    public string Label { get => _label; set => SetField(ref _label, value); }
    public string ValueText { get => _valueText; set => SetField(ref _valueText, value); }
    public double BarWidth
    {
        get => _barWidth;
        set => SetField(ref _barWidth, Math.Clamp(value * 180, 0, 180));
    }
    public Brush BarColor { get => _barColor; set => SetField(ref _barColor, value); }

    public OverlayItem(string label, string valueText, double percent)
    {
        _label = label;
        _valueText = valueText;
        _barWidth = percent * 180;
        _barColor = percent switch
        {
            < 0.5 => new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)),
            < 0.8 => new SolidColorBrush(Color.FromRgb(0xFF, 0xBB, 0x00)),
            _ => new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44))
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
