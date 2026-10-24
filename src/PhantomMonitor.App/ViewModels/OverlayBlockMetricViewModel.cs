using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

/// <summary>A per-block presentation wrapper around a live sensor row.</summary>
public sealed class OverlayBlockMetricViewModel : INotifyPropertyChanged, IDisposable
{
    private string? _displayName;
    private int? _decimals;
    private string? _color;

    public OverlayBlockMetricViewModel(OverlayMetricViewModel source, OverlayMetricOverrideConfig? metricOverride)
    {
        Source = source;
        Source.PropertyChanged += OnSourceChanged;
        UpdateOverride(metricOverride);
    }

    public OverlayMetricViewModel Source { get; }
    public string Identity => Source.OverrideIdentity;
    public string Name => string.IsNullOrWhiteSpace(_displayName) ? Source.Name : _displayName;
    public string Value => _decimals is { } decimals && Source.SeriesKey is { } key && Source.NumericValue is { } numeric
        ? OverlaySensorPrecisionFormatter.Format(Source.Value, numeric, decimals)
        : Source.Value;
    public bool ShowName => Source.ShowName;
    public bool ShowFrametimeGraph => Source.ShowFrametimeGraph;
    public IBrush NameForeground => ResolveForeground("TextOverlaySecondaryBrush");
    public IBrush ValueForeground => ResolveForeground("TextPrimaryBrush");
    public bool SupportsPrecision => Source.SeriesKey.HasValue;

    public void UpdateOverride(OverlayMetricOverrideConfig? metricOverride)
    {
        var displayName = metricOverride?.DisplayName;
        var decimals = metricOverride?.Decimals;
        var color = metricOverride?.Color;
        if (_displayName == displayName && _decimals == decimals && _color == color) return;
        _displayName = displayName;
        _decimals = decimals;
        _color = color;
        RaiseAll();
    }

    public void RefreshTheme() => Raise(nameof(NameForeground), nameof(ValueForeground));

    private IBrush ResolveForeground(string resourceKey)
    {
        var fallback = MainWindowViewModel.ResolveBrush(resourceKey);
        return string.IsNullOrWhiteSpace(_color) ? fallback : OverlayBrushHelper.Resolve(_color, fallback);
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayMetricViewModel.Name) or nameof(OverlayMetricViewModel.OverrideIdentity))
            Raise(nameof(Identity), nameof(Name));
        if (e.PropertyName is nameof(OverlayMetricViewModel.Value) or nameof(OverlayMetricViewModel.NumericValue)
            or nameof(OverlayMetricViewModel.SeriesKey))
            Raise(nameof(Value));
        if (e.PropertyName == nameof(OverlayMetricViewModel.ShowFrametimeGraph))
            Raise(nameof(ShowFrametimeGraph));
        if (e.PropertyName == nameof(OverlayMetricViewModel.ShowName))
            Raise(nameof(ShowName));
        if (e.PropertyName == nameof(OverlayMetricViewModel.SeriesKey))
            Raise(nameof(SupportsPrecision));
    }

    private void RaiseAll() => Raise(nameof(Name), nameof(Value), nameof(NameForeground), nameof(ValueForeground), nameof(SupportsPrecision));

    private void Raise(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose() => Source.PropertyChanged -= OnSourceChanged;

    public event PropertyChangedEventHandler? PropertyChanged;
}
