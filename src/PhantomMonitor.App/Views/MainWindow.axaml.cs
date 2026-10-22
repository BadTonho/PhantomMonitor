using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private OverlayWindow? _overlayWindow;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            EnsureOverlayWindow();
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            RefreshWindowPresentationState();
            ViewModel.Start();
            SyncOverlayVisibility();
        };
        Activated += (_, _) => RefreshWindowPresentationState();
        Deactivated += (_, _) => RefreshWindowPresentationState();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == Window.WindowStateProperty)
                RefreshWindowPresentationState();
        };
        Closed += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _overlayWindow?.Close();
            ViewModel.Dispose();
        };
    }

    private void EnsureOverlayWindow()
    {
        if (_overlayWindow is not null) return;

        var overlay = new OverlayWindow
        {
            DataContext = DataContext,
            Width = ViewModel.OverlayWidth,
            Opacity = ViewModel.OverlayOpacity
        };

        // Overlay fechada pelo usuário (Alt+F4): mantê-la na referência deixaria o
        // próximo Show() em janela morta. Descarta a referência e sincroniza o
        // estado do config (a janela foi fechada de verdade); SyncOverlayVisibility
        // recria a janela na próxima ativação.
        overlay.Closed += (_, _) =>
        {
            _overlayWindow = null;
            if (ViewModel.OverlayVisible)
                ViewModel.HideOverlay();
        };

        _overlayWindow = overlay;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.OverlayVisible))
            SyncOverlayVisibility();
        else if (e.PropertyName == nameof(MainWindowViewModel.OverlayWidth) && _overlayWindow is not null)
            _overlayWindow.Width = ViewModel.OverlayWidth;
        else if (e.PropertyName == nameof(MainWindowViewModel.OverlayOpacity) && _overlayWindow is not null)
            _overlayWindow.Opacity = ViewModel.OverlayOpacity;
    }

    private void SyncOverlayVisibility()
    {
        // Overlay pode ter sido fechada pelo usuário (Alt+F4): recriar antes de mostrar.
        if (ViewModel.OverlayVisible)
            EnsureOverlayWindow();
        if (_overlayWindow is null) return;
        if (ViewModel.OverlayVisible)
        {
            if (!_overlayWindow.IsVisible)
                _overlayWindow.Show();
        }
        else if (_overlayWindow.IsVisible)
        {
            _overlayWindow.Hide();
        }
    }

    private void RefreshWindowPresentationState() =>
        ViewModel.SetWindowPresentationState(IsActive, WindowState == Avalonia.Controls.WindowState.Minimized);

    private void OnStartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.Start();
    private void OnPauseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.TogglePause();
    private void OnStopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.Stop();
    private void OnToggleOverlayClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ToggleOverlay();
    private void OnOverviewClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowOverview();
    private void OnInventoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowInventory();
    private void OnCustomSensorsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowCustomSensors();
    private void OnSensorTuningClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowSensorTuning();
    private void OnSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowSettings();
    private void OnSaveSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.SaveSettings();
    private void OnAddDetectedOverlaySeriesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.AddDetectedOverlaySeries();
    private void OnAddOverlaySeriesPatternClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.AddManualOverlaySeriesPattern();
    private void OnRestoreOverlaySeriesDefaultsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.RestoreDefaultOverlaySeriesPatterns();
    private void OnAlertsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowAlerts();
    private void OnCheckUpdatesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.CheckUpdatesNow();
    private void OnOpenReleasesPageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.OpenReleasesPage();
    private void OnApplyUpdateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _ = ViewModel.ApplyUpdateAsync();
    private void OnDiagnosticsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowDiagnostics();
    private void OnRefreshDiagnosticsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.RefreshDiagnostics();
    private void OnOpenErrorLogClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            ErrorLog.EnsureLogFileExists();
            Process.Start(new ProcessStartInfo(ErrorLog.LogFilePath) { UseShellExecute = true });
            ViewModel.SetDiagnosticsMessage("Arquivo de erros aberto no aplicativo padrão.");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Diagnostics.OpenErrorLog", ex);
            ViewModel.SetDiagnosticsMessage($"Não foi possível abrir o arquivo: {ex.Message}");
        }
    }
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

    private void OnRemoveOverlaySeriesPatternClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: OverlaySeriesPatternViewModel pattern })
            ViewModel.RemoveOverlaySeriesPattern(pattern);
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
