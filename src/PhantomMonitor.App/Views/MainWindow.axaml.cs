using Avalonia.Controls;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            RefreshWindowPresentationState();
            ViewModel.Start();
        };
        Activated += (_, _) => RefreshWindowPresentationState();
        Deactivated += (_, _) => RefreshWindowPresentationState();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == Window.WindowStateProperty)
                RefreshWindowPresentationState();
        };
        Closed += (_, _) => ViewModel.Dispose();
    }

    private void RefreshWindowPresentationState() =>
        ViewModel.SetWindowPresentationState(IsActive, WindowState == Avalonia.Controls.WindowState.Minimized);

    private void OnStartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.Start();
    private void OnPauseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.TogglePause();
    private void OnStopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.Stop();
    private void OnOverviewClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowOverview();
    private void OnInventoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowInventory();
    private void OnSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowSettings();
    private void OnSaveSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.SaveSettings();
    private void OnAlertsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowAlerts();
    private void OnAddAlertRuleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.AddAlertRule();
    private void OnSaveAlertsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.SaveAlerts();

    private void OnRemoveAlertRuleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: AlertRuleEditorViewModel rule })
            ViewModel.RemoveAlertRule(rule);
    }
}
