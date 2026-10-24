using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhantomMonitor.App.ViewModels;

public sealed record OverlayHiddenMetricOption(string Identity, string Label);

public sealed class OverlayHiddenMetricsDialogViewModel : INotifyPropertyChanged
{
    private OverlayHiddenMetricOption? _selectedMetric;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<OverlayHiddenMetricOption> Metrics { get; }
    public OverlayHiddenMetricOption? SelectedMetric
    {
        get => _selectedMetric;
        set
        {
            if (ReferenceEquals(_selectedMetric, value)) return;
            _selectedMetric = value;
            Raise();
            Raise(nameof(CanRestoreSelected));
        }
    }

    public bool HasMetrics => Metrics.Count > 0;
    public bool NoMetrics => !HasMetrics;
    public bool CanRestoreSelected => SelectedMetric is not null;

    public OverlayHiddenMetricsDialogViewModel(IEnumerable<OverlayHiddenMetricOption> metrics)
    {
        Metrics = new ObservableCollection<OverlayHiddenMetricOption>(metrics);
        SelectedMetric = Metrics.FirstOrDefault();
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
