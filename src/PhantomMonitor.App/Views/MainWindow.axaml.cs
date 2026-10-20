using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

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
    private void OnCustomSensorsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowCustomSensors();
    private void OnSensorTuningClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowSensorTuning();
    private void OnSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowSettings();
    private void OnSaveSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.SaveSettings();
    private void OnAlertsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowAlerts();
    private void OnAddAlertRuleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.AddAlertRule();
    private void OnSaveAlertsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.SaveAlerts();
    private void OnAddCustomSensorClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.AddCustomSensor();
    private void OnSaveCustomSensorsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.SaveCustomSensors();
    private void OnAddSensorTuningClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.AddSensorTuning();
    private void OnSaveSensorTuningsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.SaveSensorTunings();

    private void OnRemoveCustomSensorClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CustomSensorEditorViewModel editor })
            ViewModel.RemoveCustomSensor(editor);
    }

    private void OnMoveCustomSensorUpClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveCustomSensor(sender, -1);
    private void OnMoveCustomSensorDownClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveCustomSensor(sender, 1);

    private void MoveCustomSensor(object? sender, int offset)
    {
        if (sender is Control { DataContext: CustomSensorEditorViewModel editor })
            ViewModel.MoveCustomSensor(editor, offset);
    }

    private void OnRemoveSensorTuningClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SensorTuningEditorViewModel editor })
            ViewModel.RemoveSensorTuning(editor);
    }

    private void OnMoveSensorTuningUpClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveSensorTuning(sender, -1);
    private void OnMoveSensorTuningDownClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveSensorTuning(sender, 1);

    private void MoveSensorTuning(object? sender, int offset)
    {
        if (sender is Control { DataContext: SensorTuningEditorViewModel editor })
            ViewModel.MoveSensorTuning(editor, offset);
    }

    private async void OnSelectAlertScriptClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AlertRuleEditorViewModel rule })
            return;

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Selecionar script de alerta",
                AllowMultiple = false
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
                rule.Script = path;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("AlertScriptPicker", ex);
        }
    }

    private void OnRemoveAlertRuleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: AlertRuleEditorViewModel rule })
            ViewModel.RemoveAlertRule(rule);
    }
}
