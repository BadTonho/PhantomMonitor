using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public partial class OverlayEditorWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private OverlayCanvasBlockViewModel? _dragBlock;
    private Point _dragOffset;

    public OverlayEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => UpdateEditorCanvasSize(EditorCanvasViewport.Bounds.Size);
    }

    private void OnEditorCanvasViewportSizeChanged(object? sender, SizeChangedEventArgs e)
        => UpdateEditorCanvasSize(e.NewSize);

    private void UpdateEditorCanvasSize(Size size)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.SetOverlayEditorCanvasSize(size.Width, size.Height);
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(EditorBlockCanvas).Properties.IsRightButtonPressed) return;
        var point = e.GetPosition(EditorBlockCanvas);
        var maxX = Math.Max(1, EditorBlockCanvas.Width - ViewModel.OverlayPreviewWidth);
        var maxY = Math.Max(1, EditorBlockCanvas.Height - 90);
        ViewModel.AddOverlayBlock(point.X / maxX, point.Y / maxY);
        e.Handled = true;
    }

    private void OnBlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: OverlayCanvasBlockViewModel block } control) return;
        if (e.GetCurrentPoint(control).Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            return;
        }
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

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

    private void OnBlockPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _dragBlock = null;

    private async void OnBlockDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: OverlayCanvasBlockViewModel block }) return;
        e.Handled = true;
        await EditBlockAsync(block);
    }

    private async void OnEditBlockContentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: OverlayCanvasBlockViewModel block })
            await EditBlockAsync(block);
    }

    private async Task EditBlockAsync(OverlayCanvasBlockViewModel block)
    {
        var dialog = new OverlayBlockContentDialog(ViewModel, block);
        if (await dialog.ShowDialog<bool>(this))
            ViewModel.SetOverlayBlockDefinition(block, dialog.Result, dialog.ResultGraphs, dialog.ResultTemplates);
    }

    private void OnRemoveBlockClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: OverlayCanvasBlockViewModel block })
            ViewModel.RemoveOverlayBlock(block);
    }

    private void OnSaveSettingsClick(object? sender, RoutedEventArgs e) => ViewModel.SaveSettings();
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.CancelOverlayLayoutDraft();
        base.OnClosed(e);
    }
}
