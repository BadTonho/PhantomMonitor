using System.ComponentModel;
using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

public sealed class OverlayBlockContentChoiceViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private readonly Action<bool>? _selectionChanged;

    public OverlayBlockContentChoiceViewModel(string kind, string value, string label, bool isSelected,
        Action<bool>? selectionChanged = null)
    {
        Kind = kind;
        Value = value;
        Label = label;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    public string Kind { get; }
    public string Value { get; }
    public string Label { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            _selectionChanged?.Invoke(value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class OverlayBlockItemChoiceViewModel : INotifyPropertyChanged
{
    private string _label;

    public OverlayBlockItemChoiceViewModel(string kind, string value, string label)
    {
        Kind = kind;
        Value = value;
        _label = label;
    }

    public string Kind { get; }
    public string Value { get; }
    public string Label => _label;

    public void UpdateLabel(string label)
    {
        if (_label == label) return;
        _label = label;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class OverlayMetricEditChoiceViewModel : INotifyPropertyChanged, IDisposable
{
    public OverlayMetricEditChoiceViewModel(OverlayBlockMetricViewModel metric)
    {
        Metric = metric;
        Metric.PropertyChanged += OnSourcePropertyChanged;
    }

    public OverlayBlockMetricViewModel Metric { get; }
    public OverlayMetricViewModel Source => Metric.Source;
    public string Label => $"{Metric.Name} · {Metric.Value}";
    public string Identity => Metric.Identity;
    public bool SupportsPrecision => Metric.SupportsPrecision;

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayBlockMetricViewModel.Name) or nameof(OverlayBlockMetricViewModel.Value))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose() => Metric.PropertyChanged -= OnSourcePropertyChanged;
}
