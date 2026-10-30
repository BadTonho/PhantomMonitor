using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using PhantomMonitor.App.Services.Hotkeys;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

public partial class SettingsPage : UserControl
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private bool _capturingHotkey;

    public SettingsPage()
    {
        InitializeComponent();
        OverlayHotkeyButton.AddHandler(KeyDownEvent, OnHotkeyKeyDown, RoutingStrategies.Tunnel);
        OverlayHotkeyButton.LostFocus += (_, _) => CancelHotkeyCapture();
    }

    private void OnCaptureOverlayHotkeyClick(object? sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        ViewModel.BeginHotkeyCapture();
        OverlayHotkeyButton.Focus();
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;
        e.Handled = true;
        if (e.Key == Key.Escape) { CancelHotkeyCapture(); return; }
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (HotkeyCapture.TryFormat(e.Key, e.KeyModifiers, out var shortcut))
        {
            _capturingHotkey = false;
            ViewModel.SetOverlayHotkeyDraft(shortcut);
        }
        else ViewModel.InvalidHotkeyCapture();
    }

    private void CancelHotkeyCapture()
    {
        if (!_capturingHotkey) return;
        _capturingHotkey = false;
        ViewModel.CancelHotkeyCapture();
    }

    private void OnClearOverlayHotkeyClick(object? sender, RoutedEventArgs e)
    {
        CancelHotkeyCapture();
        ViewModel.SetOverlayHotkeyDraft("");
    }

    public event EventHandler<RoutedEventArgs>? OpenOverlayEditorRequested;
    public event EventHandler<RoutedEventArgs>? OpenThemePaletteEditorRequested;

    private void OnOpenOverlayEditorClick(object? sender, RoutedEventArgs e)
        => OpenOverlayEditorRequested?.Invoke(this, e);

    private void OnAddOverlayTargetClick(object? sender, RoutedEventArgs e) => ViewModel.AddOverlayInGameTarget();
    private void OnRemoveOverlayTargetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OverlayLaunchTargetConfig target })
            ViewModel.RemoveOverlayInGameTarget(target);
    }
    private async void OnLaunchOverlayTargetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OverlayLaunchTargetConfig target })
            await ViewModel.LaunchOverlayTargetAsync(target);
    }
    private async void OnCopySteamOverlayOptionClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ViewModel.SetOverlayTargetMessage("A área de transferência não está disponível nesta sessão.");
            return;
        }

        await clipboard.SetTextAsync(ViewModel.SteamOverlayLaunchOption);
        ViewModel.SetOverlayTargetMessage("Opção copiada. Cole-a nas propriedades do jogo na Steam.");
    }
    private void OnOpenThemePaletteEditorClick(object? sender, RoutedEventArgs e)
        => OpenThemePaletteEditorRequested?.Invoke(this, e);
    private void OnCheckUpdatesClick(object? sender, RoutedEventArgs e) => ViewModel.CheckUpdatesNow();
    private void OnOpenReleasesPageClick(object? sender, RoutedEventArgs e) => ViewModel.OpenReleasesPage();
    private void OnApplyUpdateClick(object? sender, RoutedEventArgs e) => _ = ViewModel.ApplyUpdateAsync();
}
