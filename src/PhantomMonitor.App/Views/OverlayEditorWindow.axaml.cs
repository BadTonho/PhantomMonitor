using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

/// <summary>
/// Janela dedicada ao editor do overlay flutuante: prévia ao vivo + todos os
/// controles (largura, opacidade, fonte, modo compacto, categorias, frametime e
/// whitelist de séries por instância). Compartilha o mesmo MainWindowViewModel
/// da janela principal; "Salvar e aplicar" usa o mesmo SaveSettings das
/// Configurações.
/// </summary>
public partial class OverlayEditorWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public OverlayEditorWindow()
    {
        InitializeComponent();
    }

    private void OnSaveSettingsClick(object? sender, RoutedEventArgs e) => ViewModel.SaveSettings();

    private void OnAddDetectedOverlaySeriesClick(object? sender, RoutedEventArgs e) => ViewModel.AddDetectedOverlaySeries();

    private void OnAddOverlaySeriesPatternClick(object? sender, RoutedEventArgs e) => ViewModel.AddManualOverlaySeriesPattern();

    private void OnRestoreOverlaySeriesDefaultsClick(object? sender, RoutedEventArgs e) => ViewModel.RestoreDefaultOverlaySeriesPatterns();

    private void OnRemoveOverlaySeriesPatternClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ViewModels.OverlaySeriesPatternViewModel pattern })
            ViewModel.RemoveOverlaySeriesPattern(pattern);
    }
}
