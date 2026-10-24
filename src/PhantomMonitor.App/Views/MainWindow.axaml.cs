using Avalonia.Controls;
using PhantomMonitor.App.Services;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private OverlayWindow? _overlayWindow;
    private OverlayEditorWindow? _overlayEditorWindow;
    private HardwareInventoryWindow? _hardwareInventoryWindow;
    private TrayIconService? _tray;

    public MainWindow()
    {
        InitializeComponent();
        SyncNavPage("overview");
        Opened += (_, _) =>
        {
            EnsureOverlayWindow();
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            RefreshWindowPresentationState();
            ViewModel.Start();
            SyncOverlayVisibility();
            InitTrayIcon();
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
            if (_tray is { } tray)
            {
                tray.Dispose();
                _tray = null;
            }
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _overlayWindow?.Close();
            _hardwareInventoryWindow?.Close();
            ViewModel.Dispose();
        };
    }

    private void EnsureOverlayWindow()
    {
        if (_overlayWindow is not null) return;

        var overlay = new OverlayWindow(ViewModel) { Opacity = ViewModel.OverlayOpacity };

        // Overlay fechada pelo usuário (Alt+F4): mantê-la na referência deixaria o
        // próximo Show() em janela morta. Descarta a referência e sincroniza o
        // estado do config (a janela foi fechada de verdade); SyncOverlayVisibility
        // recria a janela na próxima ativação.
        overlay.UserClosed += () =>
        {
            _overlayWindow = null;
            if (ViewModel.OverlayVisible)
                ViewModel.HideOverlay();
        };

        _overlayWindow = overlay;
    }

    /// <summary>Cria o ícone da bandeja (silencioso em falha: o app continua funcional), como no WPF.</summary>
    private void InitTrayIcon()
    {
        try
        {
            _tray = new TrayIconService();
            _tray.ActivateRequested += ActivateMainWindow;
            _tray.OverlayToggleRequested += ViewModel.ToggleOverlay;
            _tray.StartupToggleRequested += () => ViewModel.StartWithWindows = !ViewModel.StartWithWindows;
            _tray.ResetStatsRequested += ViewModel.ResetSessionStats;
            _tray.PauseToggleRequested += ViewModel.TogglePause;
            _tray.ExitRequested += Close;
            _tray.IsOverlayVisible = () => ViewModel.OverlayVisible;
            _tray.IsStartupEnabled = () => ViewModel.StartWithWindows;
            _tray.IsPaused = () => ViewModel.IsPaused;
            // Seção dinâmica: perfis do OSD (fixar perfil / voltar ao automático) — pin (P3).
            _tray.SetDynamicItems(() => ViewModel.BuildTrayProfileEntries(), id => ViewModel.ApplyTrayProfileClick(id));
            RefreshTrayMenu();
        }
        catch (Exception ex)
        {
            _tray = null;
            ErrorLog.Write("TrayIcon.Init", ex);
        }
    }

    private void ActivateMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void RefreshTrayMenu()
    {
        _tray?.InvalidateMenu();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Menu da bandeja: rebuild nos estados com check (overlay/pausa/startup/perfil fixado).
        if (e.PropertyName is nameof(MainWindowViewModel.OverlayVisible)
            or nameof(MainWindowViewModel.IsPaused)
            or nameof(MainWindowViewModel.StartWithWindows)
            or nameof(MainWindowViewModel.HasOverlayProfileTag)
            or nameof(MainWindowViewModel.OverlayProfileTagText))
            RefreshTrayMenu();

        if (e.PropertyName == nameof(MainWindowViewModel.OverlayVisible))
            SyncOverlayVisibility();
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

    /// <summary>
    /// Estado selecionado da nav (F1.6 — porta do <c>IsChecked</c> do RadioButton
    /// do WPF): classe <c>.selected</c> acende NavSelectedBrush + indicador 3×14.
    /// Chamada no boot (Visão geral) e a cada clique de nav.
    /// </summary>
    private Button?[]? _navButtons;
    private static readonly string[] NavPageCodes =
        ["overview", "inventory", "customsensors", "sensortuning", "alerts", "diagnostics", "settings"];

    private void SyncNavPage(string page)
    {
        _navButtons ??=
        [
            this.FindControl<Button>("NavOverview"),
            this.FindControl<Button>("NavInventory"),
            this.FindControl<Button>("NavCustomSensors"),
            this.FindControl<Button>("NavSensorTuning"),
            this.FindControl<Button>("NavAlerts"),
            this.FindControl<Button>("NavDiagnostics"),
            this.FindControl<Button>("NavSettings"),
        ];

        for (var i = 0; i < NavPageCodes.Length; i++)
        {
            if (_navButtons[i] is not { } button) continue;
            if (NavPageCodes[i] == page)
                button.Classes.Add("selected");
            else
                button.Classes.Remove("selected");
        }
    }

    private void OnStartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.Start();
    private void OnPauseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.TogglePause();
    private void OnResetStatsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ResetSessionStats();
    private void OnStopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.Stop();
    private void OnToggleOverlayClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ToggleOverlay();
    private void OnOverviewClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SyncNavPage("overview");
        ViewModel.ShowOverview();
    }
    private void OnInventoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SyncNavPage("inventory");
        ViewModel.ShowInventory();
    }
    private void OnInventoryRowToggleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InventoryRowViewModel row } && row.CanCollapseChildren)
            row.IsExpanded = !row.IsExpanded;
    }
    private void OnOpenHardwareInventoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_hardwareInventoryWindow is null)
        {
            _hardwareInventoryWindow = new HardwareInventoryWindow(ViewModel);
            _hardwareInventoryWindow.Closed += (_, _) => _hardwareInventoryWindow = null;
            _hardwareInventoryWindow.Show(this);
            return;
        }

        _hardwareInventoryWindow.Activate();
    }
    private void OnCustomSensorsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SyncNavPage("customsensors");
        ViewModel.ShowCustomSensors();
    }
    private void OnSensorTuningClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SyncNavPage("sensortuning");
        ViewModel.ShowSensorTuning();
    }
    private void OnAlertsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SyncNavPage("alerts");
        ViewModel.ShowAlerts();
    }
    private void OnSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SyncNavPage("settings");
        ViewModel.ShowSettings();
    }
    // Editor do overlay em janela dedicada: mesma instância do VM, reabre/ativa.

    private void OnOpenOverlayEditorRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_overlayEditorWindow is null)
        {
            _overlayEditorWindow = new OverlayEditorWindow { DataContext = DataContext };
            _overlayEditorWindow.Closed += (_, _) => _overlayEditorWindow = null;
            _overlayEditorWindow.Show(this);
            return;
        }

        _overlayEditorWindow.Activate();
    }
    private void OnRestartAsAdminClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.RestartAsAdmin();
    private void OnUpdateBadgeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowSettings();
    private void OnOpenReleasesPageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.OpenReleasesPage();
    private void OnApplyUpdateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _ = ViewModel.ApplyUpdateAsync();
    private void OnDiagnosticsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SyncNavPage("diagnostics");
        ViewModel.ShowDiagnostics();
        this.FindControl<DiagnosticsPage>("DiagnosticsPageControl")?.SyncTabSelection();
    }
    private void OnAddSensorTuningClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.AddSensorTuning();
    private void OnSaveSensorTuningsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.SaveSensorTunings();

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

}
