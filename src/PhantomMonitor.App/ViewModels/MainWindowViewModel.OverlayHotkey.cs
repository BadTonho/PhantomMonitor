using Avalonia.Threading;
using PhantomMonitor.App.Services.Hotkeys;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IOverlayHotkeyBackend? _overlayHotkeyBackend;
    private string _overlayHotkeyDraft = "";
    private string _activeOverlayHotkey = "";
    private string _activeOverlayHotkeyDisplay = "";
    private string _overlayHotkeyStatus = "Escolha uma combinação ou deixe o atalho desativado.";
    private bool _isSavingSettings;
    private bool _capturingOverlayHotkey;

    public bool IsSavingSettings { get => _isSavingSettings; private set => Set(ref _isSavingSettings, value); }
    public string OverlayHotkeyStatus { get => _overlayHotkeyStatus; private set => Set(ref _overlayHotkeyStatus, value); }
    public string OverlayHotkeyButtonText => _capturingOverlayHotkey
        ? "Pressione a combinação (Esc cancela)"
        : _overlayHotkeyDraft.Length == 0 ? "Definir atalho" : _overlayHotkeyDraft;

    public void BeginHotkeyCapture()
    {
        _capturingOverlayHotkey = true;
        Raise(nameof(OverlayHotkeyButtonText));
        OverlayHotkeyStatus = "Pressione uma tecla com Ctrl, Alt, Shift ou Win, ou uma tecla F1–F24.";
    }

    public void CancelHotkeyCapture()
    {
        _capturingOverlayHotkey = false;
        Raise(nameof(OverlayHotkeyButtonText));
        OverlayHotkeyStatus = "Captura cancelada.";
    }

    public void InvalidHotkeyCapture() => OverlayHotkeyStatus = "Combinação inválida. Use um modificador com letra ou número, ou uma tecla F1–F24.";

    public void SetOverlayHotkeyDraft(string shortcut)
    {
        _capturingOverlayHotkey = false;
        _overlayHotkeyDraft = shortcut;
        Raise(nameof(OverlayHotkeyButtonText));
        OverlayHotkeyStatus = shortcut.Length == 0 ? "O atalho será desativado ao salvar." : "Atalho escolhido. Selecione Salvar e aplicar.";
    }

    public async void InitializeOverlayHotkey()
    {
        if (_disposed) return;
        if (OperatingSystem.IsWindows())
        {
            _hotkeys ??= new GlobalHotkeyService();
            _overlayHotkeyBackend = new WindowsOverlayHotkeyBackend(_hotkeys, OnOverlayHotkeyPressed);
        }
        else if (OperatingSystem.IsLinux())
        {
            var isWayland = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            _overlayHotkeyBackend = isWayland
                ? new WaylandOverlayHotkeyBackend(OnOverlayHotkeyPressed, OnOverlayHotkeyUnavailable)
                : new X11OverlayHotkeyBackend(OnOverlayHotkeyPressed, OnOverlayHotkeyUnavailable);
        }

        if (_config.ToggleOverlayHotkey.Length == 0) return;
        IsSavingSettings = true;
        try
        {
            var result = await ApplyOverlayHotkeyAsync(_config.ToggleOverlayHotkey);
            if (!_disposed) OverlayHotkeyStatus = result.Success ? $"Atalho ativo: {result.Display}." : result.Message;
        }
        finally { IsSavingSettings = false; }
    }

    private void OnOverlayHotkeyPressed() => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed && !_capturingOverlayHotkey && !IsSavingSettings) ToggleOverlay();
    });

    private void OnOverlayHotkeyUnavailable(string message) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed) return;
        _activeOverlayHotkey = "";
        _activeOverlayHotkeyDisplay = "";
        OverlayHotkeyStatus = message;
    });

    private async Task<HotkeyResult> ApplyOverlayHotkeyAsync(string shortcut)
    {
        if (_overlayHotkeyBackend is null)
            return shortcut.Length == 0 ? HotkeyResult.Active("") : HotkeyResult.Failed("Atalhos globais não estão disponíveis nesta sessão.");
        try
        {
            var result = await _overlayHotkeyBackend.ApplyAsync(shortcut);
            if (result.Success) { _activeOverlayHotkey = shortcut; _activeOverlayHotkeyDisplay = result.Display; }
            return result;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("OverlayHotkey.Apply", ex);
            return HotkeyResult.Failed("Não foi possível configurar o atalho global nesta sessão.");
        }
    }
}
