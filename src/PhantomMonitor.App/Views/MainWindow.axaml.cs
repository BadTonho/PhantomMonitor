using Avalonia.Controls;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ViewModel.Start();
        Closed += (_, _) => ViewModel.Dispose();
    }

    private void OnStartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.Start();
    private void OnPauseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.TogglePause();
    private void OnStopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.Stop();
    private void OnOverviewClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowOverview();
    private void OnInventoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowInventory();
}
