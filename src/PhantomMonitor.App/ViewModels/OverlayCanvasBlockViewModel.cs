using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

/// <summary>Bloco livre do editor; a configuração e o conteúdo são rascunhos até salvar.</summary>
public sealed class OverlayCanvasBlockViewModel : INotifyPropertyChanged
{
    private readonly Action<string, double, double> _move;
    private readonly Dictionary<string, OverlayBlockMetricViewModel> _metricViewModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OverlayCustomLineViewModel> _customLines = new(StringComparer.OrdinalIgnoreCase);
    private double _canvasWidth = 960;
    private double _canvasHeight = 760;
    private double _editorPreviewWidth;
    private double _editorPreviewHeight;

    public OverlayCanvasBlockViewModel(OverlayBlockConfig config, MainWindowViewModel owner,
        Action<string, double, double> move)
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
            var labels = Content.OfType<OverlayBlockMetricViewModel>().Select(row => row.Name)
                .Concat(Content.OfType<OverlayGraphRowViewModel>().Select(_ => "Gráfico"))
                .Concat(Content.OfType<OverlayFrametimeGraphViewModel>().Select(_ => "Frametime"))
                .Concat(Content.OfType<OverlayTemplateRowViewModel>().Select(_ => "Template"))
                .Concat(Content.OfType<OverlayCustomLineViewModel>().Select(_ => "Texto"))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            return labels.Length == 0 ? "Bloco do overlay" : string.Join(" · ", labels);
        }
    }
    public MainWindowViewModel Owner { get; }
    internal bool IsPreview { get; private set; }
    public ObservableCollection<object> Content { get; } = [];
    public bool HasContent => Config.Items.Count > 0 && Content.Count > 0;
    public bool IsEditorVisible => true;
    public bool IsRenderable => HasContent && Owner.OverlayPanelVisible;
    public double X => Config.X;
    public double Y => Config.Y;
    public double PreviewBlockWidth => ScalePreviewSize(_editorPreviewWidth,
        Owner.OverlayCanvasWidth, _canvasWidth, OverlayPositionMath.ReferenceWidth);
    public double PreviewBlockHeight => ScalePreviewSize(_editorPreviewHeight,
        Owner.OverlayCanvasHeight, _canvasHeight, OverlayPositionMath.ReferenceHeight);
    public double PreviewOriginOffsetXReference => GetPreviewOriginOffsetInReference(Owner.FollowOffsetX,
        Owner.OverlayCanvasWidth, OverlayPositionMath.ReferenceWidth);
    public double PreviewOriginOffsetYReference => GetPreviewOriginOffsetInReference(Owner.FollowOffsetY,
        Owner.OverlayCanvasHeight, OverlayPositionMath.ReferenceHeight);
    public double CanvasX => OverlayPositionMath.ToDisplayPosition(X, _canvasWidth, PreviewBlockWidth,
        OverlayPositionMath.ReferenceWidth, PreviewOriginOffsetXReference);
    public double CanvasY => OverlayPositionMath.ToDisplayPosition(Y, _canvasHeight, PreviewBlockHeight,
        OverlayPositionMath.ReferenceHeight, PreviewOriginOffsetYReference);
    public double PresentationWidth => IsPreview ? Owner.OverlayPreviewWidth : Owner.OverlayWidth;
    public IBrush PresentationBackgroundBrush => IsPreview ? Owner.OverlayPreviewBackgroundBrush : Owner.OverlayBackgroundBrush;
    public IBrush PresentationBorderBrush => IsPreview ? Owner.OverlayPreviewBorderBrush : Owner.OverlayPanelBorderBrush;
    public Thickness PresentationBorderThickness => IsPreview ? Owner.OverlayPreviewBorderThickness : Owner.OverlayBorderThickness;
    public FontFamily PresentationNameFontFamily => IsPreview ? Owner.OverlayPreviewNameFontFamily : Owner.OverlayMetricNameFontFamily;
    public FontFamily PresentationValueFontFamily => IsPreview ? Owner.OverlayPreviewValueFontFamily : Owner.OverlayMetricValueFontFamily;
    public double PresentationNameFontSize => IsPreview ? Owner.OverlayPreviewNameFontSize : Owner.OverlayMetricNameFontSize;
    public double PresentationValueFontSize => IsPreview ? Owner.OverlayPreviewValueFontSize : Owner.OverlayMetricValueFontSize;
    public Thickness PresentationOuterMargin => IsPreview ? Owner.OverlayPreviewOuterMargin : Owner.OverlayOuterMargin;
    public Thickness PresentationPadding => IsPreview ? Owner.OverlayPreviewPadding : Owner.OverlayPadding;
    public double PresentationContentSpacing => IsPreview ? Owner.OverlayPreviewContentSpacing : Owner.OverlayContentSpacing;
    public Thickness PresentationRowMargin => IsPreview ? Owner.OverlayPreviewRowMargin : Owner.OverlayRowMargin;
    public double PresentationFrametimeGraphHeight => IsPreview ? Owner.OverlayPreviewFrametimeGraphHeight : Owner.OverlayFrametimeGraphHeight;
    public IBrush PresentationAccentBrush => Owner.OverlayAccentBrush;

    public void SetCanvasSize(double width, double height)
    {
        _canvasWidth = Math.Max(240, width);
        _canvasHeight = Math.Max(120, height);
        RaisePosition();
    }

    internal void MarkAsPreview() => IsPreview = true;

    public void RefreshCanvasPosition() => RaisePosition();

    private static double ScalePreviewSize(double size, double sourceAreaSize, double displayAreaSize,
        double referenceAreaSize)
    {
        var referenceSize = OverlayPositionMath.ScaleOffset(size, sourceAreaSize, referenceAreaSize);
        return OverlayPositionMath.FromReference(referenceSize, referenceAreaSize, displayAreaSize);
    }

    private static double GetPreviewOriginOffsetInReference(double offset, double referenceSize, double previewSize)
        => OperatingSystem.IsWindows()
            ? OverlayPositionMath.ScaleOffset(offset, referenceSize, previewSize)
            : 0;

    public void SetEditorPreviewSize(double width, double height)
    {
        if (width <= 0 || height <= 0
            || Math.Abs(_editorPreviewWidth - width) < 0.1 && Math.Abs(_editorPreviewHeight - height) < 0.1)
            return;

        _editorPreviewWidth = width;
        _editorPreviewHeight = height;
        RaisePosition();
    }

    public void SetNormalizedPosition(double x, double y)
    {
        x = double.IsFinite(x) ? x : 0;
        y = double.IsFinite(y) ? y : 0;
        if (Math.Abs(Config.X - x) < 0.0001 && Math.Abs(Config.Y - y) < 0.0001) return;
        Config.X = x;
        Config.Y = y;
        RaisePosition();
    }

    public void MoveTo(double x, double y) => _move(Key,
        double.IsFinite(x) ? x : 0, double.IsFinite(y) ? y : 0);

    public void NotifyContentChanged()
    {
        Raise(nameof(Title));
        Raise(nameof(HasContent));
        Raise(nameof(IsRenderable));
    }

    public OverlayBlockMetricViewModel GetMetricViewModel(OverlayMetricViewModel source)
    {
        var identity = source.OverrideIdentity;
        var metricOverride = Config.MetricOverrides.FirstOrDefault(item =>
            item.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase));
        var showGpuModel = IsPreview ? Owner.OverlayShowGpuModelSetting : Owner.OverlayShowGpuModel;
        if (_metricViewModels.TryGetValue(identity, out var existing))
        {
            if (ReferenceEquals(existing.Source, source))
            {
                existing.UpdateOverride(metricOverride);
                existing.SetShowGpuModel(showGpuModel);
                return existing;
            }
            existing.Dispose();
        }

        var created = new OverlayBlockMetricViewModel(source, metricOverride, showGpuModel);
        _metricViewModels[identity] = created;
        return created;
    }

    public void RefreshMetricThemes()
    {
        foreach (var metric in _metricViewModels.Values)
            metric.RefreshTheme();
    }

    public OverlayCustomLineViewModel GetCustomLine(OverlayCustomLineConfig config, IReadOnlyList<HardwareIdentity> devices)
    {
        if (!_customLines.TryGetValue(config.Id, out var line))
            _customLines[config.Id] = line = new OverlayCustomLineViewModel(this, config.Id);
        line.Update(config, devices);
        return line;
    }

    public void RefreshPresentation()
    {
        foreach (var line in _customLines.Values) line.RefreshPresentation();
        Raise(nameof(PresentationWidth));
        Raise(nameof(PresentationBackgroundBrush));
        Raise(nameof(PresentationBorderBrush));
        Raise(nameof(PresentationBorderThickness));
        Raise(nameof(PresentationNameFontFamily));
        Raise(nameof(PresentationValueFontFamily));
        Raise(nameof(PresentationNameFontSize));
        Raise(nameof(PresentationValueFontSize));
        Raise(nameof(PresentationOuterMargin));
        Raise(nameof(PresentationPadding));
        Raise(nameof(PresentationContentSpacing));
        Raise(nameof(PresentationRowMargin));
        Raise(nameof(PresentationFrametimeGraphHeight));
        Raise(nameof(PresentationAccentBrush));
    }

    public void DisposeMetrics()
    {
        foreach (var metric in _metricViewModels.Values)
            metric.Dispose();
        _metricViewModels.Clear();
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
        while (Content.Count > position)
        {
            if (Content[^1] is OverlayBlockMetricViewModel metric)
            {
                metric.Dispose();
                if (_metricViewModels.TryGetValue(metric.Identity, out var cached) && ReferenceEquals(cached, metric))
                    _metricViewModels.Remove(metric.Identity);
            }
            else if (Content[^1] is OverlayCustomLineViewModel customLine
                && _customLines.TryGetValue(customLine.Id, out var cachedLine) && ReferenceEquals(cachedLine, customLine))
                _customLines.Remove(customLine.Id);
            Content.RemoveAt(Content.Count - 1);
        }
        NotifyContentChanged();
    }

    private void RaisePosition()
    {
        Raise(nameof(X));
        Raise(nameof(Y));
        Raise(nameof(PreviewBlockWidth));
        Raise(nameof(PreviewBlockHeight));
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
