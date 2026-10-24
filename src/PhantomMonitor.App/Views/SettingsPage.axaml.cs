using Avalonia.Interactivity;
using Avalonia.Controls;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public partial class SettingsPage : UserControl
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public SettingsPage() => InitializeComponent();

    public event EventHandler<RoutedEventArgs>? OpenOverlayEditorRequested;
    public event EventHandler<RoutedEventArgs>? OpenThemePaletteEditorRequested;

    private void OnOpenOverlayEditorClick(object? sender, RoutedEventArgs e)
        => OpenOverlayEditorRequested?.Invoke(this, e);
    private void OnOpenThemePaletteEditorClick(object? sender, RoutedEventArgs e)
        => OpenThemePaletteEditorRequested?.Invoke(this, e);
    private void OnCheckUpdatesClick(object? sender, RoutedEventArgs e) => ViewModel.CheckUpdatesNow();
    private void OnOpenReleasesPageClick(object? sender, RoutedEventArgs e) => ViewModel.OpenReleasesPage();
    private void OnApplyUpdateClick(object? sender, RoutedEventArgs e) => _ = ViewModel.ApplyUpdateAsync();
    private void OnSaveSettingsClick(object? sender, RoutedEventArgs e) => ViewModel.SaveSettings();
}
