using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

public partial class AlertsPage : UserControl
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public AlertsPage() => InitializeComponent();

    private void OnStartClick(object? sender, RoutedEventArgs e) => ViewModel.Start();
    private void OnPauseClick(object? sender, RoutedEventArgs e) => ViewModel.TogglePause();
    private void OnStopClick(object? sender, RoutedEventArgs e) => ViewModel.Stop();
    private void OnAddAlertRuleClick(object? sender, RoutedEventArgs e) => ViewModel.AddAlertRule();
    private void OnSaveAlertsClick(object? sender, RoutedEventArgs e) => ViewModel.SaveAlerts();

    private async void OnSelectAlertScriptClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AlertRuleEditorViewModel rule }) return;
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Selecionar script de alerta",
                AllowMultiple = false
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) rule.Script = path;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("AlertScriptPicker", ex);
        }
    }

    private void OnRemoveAlertRuleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: AlertRuleEditorViewModel rule })
            ViewModel.RemoveAlertRule(rule);
    }
}
