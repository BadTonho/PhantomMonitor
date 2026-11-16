using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public partial class InventoryPage : UserControl
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public event EventHandler<RoutedEventArgs>? OpenHardwareInventoryRequested;

    public InventoryPage() => InitializeComponent();

    private void OnStartClick(object? sender, RoutedEventArgs e) => ViewModel.Start();
    private void OnPauseClick(object? sender, RoutedEventArgs e) => ViewModel.TogglePause();
    private void OnStopClick(object? sender, RoutedEventArgs e) => ViewModel.Stop();
    private void OnResetStatsClick(object? sender, RoutedEventArgs e) => ViewModel.ResetSessionStats();
    private void OnClearInventoryFilterClick(object? sender, RoutedEventArgs e) => ViewModel.ClearInventoryFilter();

    private void OnInventoryRowToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InventoryRowViewModel row } && row.CanCollapseChildren)
            row.IsExpanded = !row.IsExpanded;
    }

    private void OnOpenHardwareInventoryClick(object? sender, RoutedEventArgs e)
        => OpenHardwareInventoryRequested?.Invoke(this, e);
}
