using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

public partial class SettingsPage : UserControl
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public SettingsPage() => InitializeComponent();

    public event EventHandler<RoutedEventArgs>? OpenOverlayEditorRequested;
    public event EventHandler<RoutedEventArgs>? OpenThemePaletteEditorRequested;

    private void OnOpenOverlayEditorClick(object? sender, RoutedEventArgs e)
        => OpenOverlayEditorRequested?.Invoke(this, e);

    private async void OnBrowseOverlayTargetClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Selecionar executável do jogo",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Executáveis do Windows") { Patterns = ["*.exe"] }
                ]
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                ViewModel.OverlayTargetPathText = path;
                ViewModel.SetOverlayTargetMessage("Executável selecionado. Clique em Adicionar e depois em Salvar e aplicar.");
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("OverlayTargetPicker", ex);
            ViewModel.SetOverlayTargetMessage($"Não foi possível selecionar o executável: {ex.Message}");
        }
    }

    private void OnAddOverlayTargetClick(object? sender, RoutedEventArgs e) => ViewModel.AddOverlayInGameTarget();
    private void OnRemoveOverlayTargetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OverlayLaunchTargetConfig target })
            ViewModel.RemoveOverlayInGameTarget(target);
    }
    private void OnLaunchOverlayTargetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OverlayLaunchTargetConfig target })
            ViewModel.LaunchOverlayTarget(target);
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
    private void OnSaveSettingsClick(object? sender, RoutedEventArgs e) => ViewModel.SaveSettings();
}
