using System.ComponentModel;

namespace PhantomMonitor.App.ViewModels;

/// <summary>A rule header or one live sensor row expanded from that rule.</summary>
public sealed class OverlayBlockOrderRowViewModel : INotifyPropertyChanged, IDisposable
{
    public OverlayBlockOrderRowViewModel(OverlayBlockItemChoiceViewModel group, OverlayMetricEditChoiceViewModel? metric = null)
    {
        Group = group;
        Metric = metric;
        Group.PropertyChanged += OnGroupChanged;
        if (Metric is not null) Metric.PropertyChanged += OnMetricChanged;
    }

    public OverlayBlockItemChoiceViewModel Group { get; }
    public OverlayMetricEditChoiceViewModel? Metric { get; }
    public bool IsMetric => Metric is not null;
    public bool IsGroup => Metric is null;
    public string Kind => IsMetric ? "sensor" : Group.Kind;
    public string Label => IsMetric ? Metric!.Label : Group.Label;
    public string GroupKey => OverlayBlockContentDialogViewModel.GetMetricOrderKey(Group.Kind, Group.Value);
    public string Identity => Metric?.Identity ?? Group.Value;
    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Metric is null && e.PropertyName == nameof(OverlayBlockItemChoiceViewModel.Label))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }
    private void OnMetricChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayMetricEditChoiceViewModel.Label))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }
    public void Dispose()
    {
        Group.PropertyChanged -= OnGroupChanged;
        if (Metric is not null) Metric.PropertyChanged -= OnMetricChanged;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
