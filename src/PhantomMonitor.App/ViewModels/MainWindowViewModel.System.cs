using Avalonia.Threading;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

// Sistema (F1.5): inicialização com o SO (registro Run no Windows / XDG autostart no
// Linux), hotkeys globais (Windows: RegisterHotKey numa janela message-only pelo Core)
// e as ações de alerta por SO (AlertActionFactory). O pin de perfis pela bandeja vive
// no partial OverlayProfiles; o serviço de tray em si fica em App/Services/TrayIconService.
public sealed partial class MainWindowViewModel
{
    private readonly IAlertActions _alertActions = AlertActionFactory.Create();
    private readonly IStartupManager? _startupManager = StartupManagerFactory.Create();
    private GlobalHotkeyService? _hotkeys;

    private bool _startWithWindows;

    /// <summary>Estado real da inicialização automática (o SO é a fonte de verdade, como no WPF).</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetStartWithWindows(value);
    }

    /// <summary>Suporte existe no SO atual (checkbox Some na sidebar e item Some no menu da bandeja).</summary>
    public bool StartupSupported => _startupManager is not null;

    private void RefreshStartWithWindows()
    {
        try
        {
            _startWithWindows = _startupManager?.IsEnabled() ?? false;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Startup.Refresh", ex);
            _startWithWindows = false;
        }
        Raise(nameof(StartWithWindows));
    }

    /// <summary>
    /// Caminho do checkbox da sidebar (TwoWay) e do item da bandeja: aplica no SO e
    /// reflete o estado REAL (pode falhar sob política restrita — o check volta ao
    /// estado real, mesma conduta do checkbox do WPF).
    /// </summary>
    private void SetStartWithWindows(bool enabled)
    {
        var applied = _startupManager?.SetEnabled(enabled) ?? false;
        _startWithWindows = _startupManager?.IsEnabled() ?? false;
        Raise(nameof(StartWithWindows));

        if (!applied)
            ErrorLog.Write("Startup",
                $"Não foi possível {(enabled ? "ativar" : "desativar")} a inicialização com o sistema.");
    }

    /// <summary>
    /// Hotkeys globais (P6.7, Windows): overlay (0x5048) e pausa (0x5049) conforme o
    /// config. Chamar em arranque e na reconfiguração — Registrar da mesma id troca a
    /// combinação, como o ApplyHotkey do WPF. Callbacks chegam na thread do serviço e
    /// são publicados no dispatcher da UI.
    /// </summary>
    public void ApplyHotkeys()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_disposed) return;

        try
        {
            _hotkeys ??= new GlobalHotkeyService();
            _hotkeys.Register(GlobalHotkeyService.DefaultHotkeyId, _config.ToggleOverlayHotkey,
                () => Dispatcher.UIThread.Post(ToggleOverlay));
            _hotkeys.Register(GlobalHotkeyService.PauseHotkeyId, _config.PauseMonitorHotkey,
                () => Dispatcher.UIThread.Post(TogglePause));
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Hotkeys.Apply", ex);
        }
    }
}
