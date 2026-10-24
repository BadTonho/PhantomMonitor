using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public partial class OverlayHiddenMetricsDialog : Window
{
    private OverlayHiddenMetricsDialogViewModel ViewModel => (OverlayHiddenMetricsDialogViewModel)DataContext!;

    public OverlayHiddenMetricsDialog()
    {
        InitializeComponent();
    }

    public OverlayHiddenMetricsDialog(IEnumerable<OverlayHiddenMetricOption> metrics) : this()
    {
        DataContext = new OverlayHiddenMetricsDialogViewModel(metrics);
    }

    private void OnRestoreSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedMetric is { } selected)
            Close(new[] { selected.Identity });
    }

    private void OnRestoreAllClick(object? sender, RoutedEventArgs e)
        => Close(ViewModel.Metrics.Select(metric => metric.Identity).ToArray());

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close((string[]?)null);
}
