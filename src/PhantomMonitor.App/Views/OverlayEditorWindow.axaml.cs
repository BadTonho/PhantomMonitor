using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia;
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
    private OverlayCanvasBlockViewModel? _dragBlock;
    private Point _dragOffset;

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

    private void OnBlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || sender is not Control { DataContext: OverlayCanvasBlockViewModel block } control)
            return;

        _dragBlock = block;
        var point = e.GetPosition(EditorBlockCanvas);
        _dragOffset = new Point(point.X - block.CanvasX, point.Y - block.CanvasY);
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnBlockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragBlock is null || !ReferenceEquals(e.Pointer.Captured, sender)) return;
        var point = e.GetPosition(EditorBlockCanvas);
        var maxX = Math.Max(1, EditorBlockCanvas.Width - ViewModel.OverlayPreviewWidth);
        var maxY = Math.Max(1, EditorBlockCanvas.Height - 90);
        ViewModel.ApplyOverlayBlockPosition(
            _dragBlock.Key,
            (point.X - _dragOffset.X) / maxX,
            (point.Y - _dragOffset.Y) / maxY);
        e.Handled = true;
    }

    private void OnBlockPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragBlock is null) return;
        e.Pointer.Capture(null);
        _dragBlock = null;
        e.Handled = true;
    }

    private void OnBlockPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragBlock = null;
    }
}
