using System.Collections.ObjectModel;
using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

/// <summary>Grupo de métricas que pode ser movido como um único bloco do OSD.</summary>
public sealed class OverlayCanvasBlockViewModel
{
    private readonly Action<string, double, double> _move;
    private double _x;
    private double _y;
    private double _canvasWidth = 960;
    private double _canvasHeight = 760;
    private bool _lastHasRows;
    private bool _lastHasExtras;

    public OverlayCanvasBlockViewModel(string key, string title, MainWindowViewModel owner, Action<string, double, double> move)
    {
        Key = key;
        Title = title;
        Owner = owner;
        _move = move;
    }

    public string Key { get; }
    public string Title { get; }
    public MainWindowViewModel Owner { get; }
    public ObservableCollection<OverlayMetricViewModel> Rows { get; } = [];
    public ObservableCollection<object> Extras { get; } = [];
    public bool HasRows => Rows.Count > 0;
    public bool HasExtras => Extras.Count > 0;
    public bool HasContent => HasRows || HasExtras;
    public bool IsRenderable => HasContent && Owner.OverlayPanelVisible;

    public double X
    {
        get => _x;
        private set { if (Math.Abs(_x - value) < 0.0001) return; _x = value; RaisePosition(); }
    }

    public double Y
    {
        get => _y;
        private set { if (Math.Abs(_y - value) < 0.0001) return; _y = value; RaisePosition(); }
    }

    public double CanvasX => X * Math.Max(0, _canvasWidth - Owner.OverlayPreviewWidth);
    public double CanvasY => Y * Math.Max(0, _canvasHeight - 90);

    public void SetCanvasSize(double width, double height)
    {
        _canvasWidth = Math.Max(240, width);
        _canvasHeight = Math.Max(120, height);
        RaisePosition();
    }

    public void RefreshCanvasPosition() => RaisePosition();

    public void SetNormalizedPosition(double x, double y)
    {
        X = Math.Clamp(x, 0, 1);
        Y = Math.Clamp(y, 0, 1);
    }

    public void MoveTo(double x, double y) => _move(Key, Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));

    public void NotifyRowsChanged()
    {
        var hasRows = HasRows;
        var hasExtras = HasExtras;
        if (_lastHasRows != hasRows) Raise(nameof(HasRows));
        if (_lastHasExtras != hasExtras) Raise(nameof(HasExtras));
        if ((_lastHasRows || _lastHasExtras) != (hasRows || hasExtras))
        {
            Raise(nameof(HasContent));
            Raise(nameof(IsRenderable));
        }
        _lastHasRows = hasRows;
        _lastHasExtras = hasExtras;
    }

    private void RaisePosition()
    {
        Raise(nameof(X));
        Raise(nameof(Y));
        Raise(nameof(CanvasX));
        Raise(nameof(CanvasY));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new(name));
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

internal static class OverlayBlockKeys
{
    public const string Cpu = "cpu";
    public const string Gpu = "gpu";
    public const string Ram = "ram";
    public const string Disk = "disk";
    public const string Network = "network";
    public const string Fans = "fans";
    public const string Motherboard = "motherboard";
    public const string Fps = "fps";
    public const string Extras = "extras";

    public static readonly string[] Ordered = [Cpu, Gpu, Ram, Disk, Network, Fans, Motherboard, Fps, Extras];

    public static string ForIdentity(string identity)
    {
        if (identity.Equals("CPU", StringComparison.OrdinalIgnoreCase)) return Cpu;
        if (identity.Equals("GPU", StringComparison.OrdinalIgnoreCase)) return Gpu;
        if (identity.Equals("RAM", StringComparison.OrdinalIgnoreCase)) return Ram;
        if (identity.Equals("DISCO", StringComparison.OrdinalIgnoreCase)) return Disk;
        if (identity.Equals("REDE", StringComparison.OrdinalIgnoreCase)) return Network;
        if (identity.StartsWith("FAN", StringComparison.OrdinalIgnoreCase)
            || identity.Equals("GPU FAN", StringComparison.OrdinalIgnoreCase)
            || identity.Equals("VENTOINHA", StringComparison.OrdinalIgnoreCase)) return Fans;
        if (identity.Equals("PLACA", StringComparison.OrdinalIgnoreCase)) return Motherboard;
        if (identity.Equals("JOGO", StringComparison.OrdinalIgnoreCase)) return Fps;
        if (!SensorKey.TryParse(identity, out var key)) return Extras;

        return key.Type switch
        {
            SensorType.CpuTotal or SensorType.CpuPerCore or SensorType.CpuTemperature or SensorType.CpuClock or SensorType.CpuPower or SensorType.CpuVoltage => Cpu,
            SensorType.GpuCore or SensorType.GpuTemperature or SensorType.GpuMemory or SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal or SensorType.GpuFan or SensorType.GpuPower or SensorType.GpuCoreClock or SensorType.GpuMemoryClock or SensorType.GpuVoltage or SensorType.GpuHotSpot or SensorType.GpuVramTemperature => Gpu,
            SensorType.RamUsage or SensorType.RamUsed or SensorType.RamTotal or SensorType.RamTemperature => Ram,
            SensorType.DiskRead or SensorType.DiskWrite or SensorType.DiskTotalRead or SensorType.DiskTotalWritten or SensorType.DiskUsage or SensorType.DiskHealth or SensorType.DiskTemperature => Disk,
            SensorType.NetworkUp or SensorType.NetworkDown or SensorType.NetworkUpTotal or SensorType.NetworkDownTotal => Network,
            SensorType.FanSpeed or SensorType.GpuFan => Fans,
            SensorType.MotherboardTemp or SensorType.VrmTemperature or SensorType.RailVoltage => Motherboard,
            SensorType.Fps or SensorType.FrametimeMs => Fps,
            _ => Extras
        };
    }
}
