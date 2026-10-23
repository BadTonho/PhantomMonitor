using System.Collections.ObjectModel;
using System.ComponentModel;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

/// <summary>Bloco livre do editor; a configuração e o conteúdo são rascunhos até salvar.</summary>
public sealed class OverlayCanvasBlockViewModel : INotifyPropertyChanged
{
    private readonly Action<string, double, double> _move;
    private double _canvasWidth = 960;
    private double _canvasHeight = 760;

    public OverlayCanvasBlockViewModel(OverlayBlockConfig config, MainWindowViewModel owner, Action<string, double, double> move)
    {
        Config = config;
        Owner = owner;
        _move = move;
    }

    public OverlayBlockConfig Config { get; }
    public string Key => Config.Id;
    public string Title
    {
        get
        {
            if (Content.Count == 0) return "Novo bloco · duplo clique para configurar";
            var labels = Content.OfType<OverlayMetricViewModel>().Select(row => row.Name)
                .Concat(Content.OfType<OverlayGraphRowViewModel>().Select(_ => "Gráfico"))
                .Concat(Content.OfType<OverlayFrametimeGraphViewModel>().Select(_ => "Frametime"))
                .Concat(Content.OfType<OverlayTemplateRowViewModel>().Select(_ => "Template"))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            return labels.Length == 0 ? "Bloco do overlay" : string.Join(" · ", labels);
        }
    }
    public MainWindowViewModel Owner { get; }
    public ObservableCollection<object> Content { get; } = [];
    public bool HasContent => Config.Items.Count > 0 && Content.Count > 0;
    public bool IsEditorVisible => true;
    public bool IsRenderable => HasContent && Owner.OverlayPanelVisible;
    public double X => Config.X;
    public double Y => Config.Y;
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
        x = Math.Clamp(x, 0, 1);
        y = Math.Clamp(y, 0, 1);
        if (Math.Abs(Config.X - x) < 0.0001 && Math.Abs(Config.Y - y) < 0.0001) return;
        Config.X = x;
        Config.Y = y;
        RaisePosition();
    }

    public void MoveTo(double x, double y) => _move(Key, Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));

    public void NotifyContentChanged()
    {
        Raise(nameof(Title));
        Raise(nameof(HasContent));
        Raise(nameof(IsRenderable));
    }

    public void ReconcileContent(IReadOnlyList<object> desired)
    {
        var position = 0;
        for (var index = 0; index < desired.Count; index++)
        {
            var item = desired[index];
            if (position < Content.Count && ReferenceEquals(Content[position], item))
            {
                position++;
                continue;
            }

            var found = -1;
            for (var scan = position + 1; scan < Content.Count; scan++)
                if (ReferenceEquals(Content[scan], item)) { found = scan; break; }
            if (found >= 0) Content.Move(found, position);
            else Content.Insert(position, item);
            position++;
        }
        while (Content.Count > position) Content.RemoveAt(Content.Count - 1);
        NotifyContentChanged();
    }

    private void RaisePosition()
    {
        Raise(nameof(X));
        Raise(nameof(Y));
        Raise(nameof(CanvasX));
        Raise(nameof(CanvasY));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new(name));
    public event PropertyChangedEventHandler? PropertyChanged;
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

    public static string[] Ordered => [Cpu, Gpu, Ram, Disk, Network, Fans, Motherboard, Fps, Extras];

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
