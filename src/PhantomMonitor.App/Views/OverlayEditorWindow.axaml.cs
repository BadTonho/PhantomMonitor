using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PhantomMonitor.App.ViewModels;
using System.Diagnostics;

namespace PhantomMonitor.App.Views;

public partial class OverlayEditorWindow : Window
{
    private const double AppearancePanelWidth = 360;
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private OverlayCanvasBlockViewModel? _dragBlock;
    private Point _dragOffset;
    private bool _appearanceSettingsOpen;
    private bool _appearanceSettingsTransitioning;
    private double _windowWidthBeforeAppearanceSettings;
    private TranslateTransform AppearancePanelTransform => (TranslateTransform)AppearanceSettingsPanel.RenderTransform!;

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
            viewModel.SetOverlayEditorCanvasSize(
                size.Width - (_appearanceSettingsOpen ? AppearancePanelWidth : 0), size.Height);
    }

    private async void OnAppearanceSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_appearanceSettingsTransitioning) return;

        _appearanceSettingsTransitioning = true;
        AppearanceSettingsButton.IsEnabled = false;
        try
        {
            if (_appearanceSettingsOpen)
            {
                await AnimateAppearancePanelAsync(0, -AppearancePanelWidth);
                if (!IsVisible) return;

                _appearanceSettingsOpen = false;
                AppearanceSettingsHost.Width = 0;
                AppearanceSettingsHost.IsVisible = false;
                Width = _windowWidthBeforeAppearanceSettings;
                AppearanceSettingsButton.Content = "Configurações";
                await Task.Delay(16);
                UpdateEditorCanvasSize(EditorCanvasViewport.Bounds.Size);
            }
            else
            {
                _windowWidthBeforeAppearanceSettings = Width;
                _appearanceSettingsOpen = true;
                AppearancePanelTransform.X = -AppearancePanelWidth;
                AppearanceSettingsHost.Width = AppearancePanelWidth;
                AppearanceSettingsHost.IsVisible = true;
                AppearanceSettingsButton.Content = "Voltar";
                Width = _windowWidthBeforeAppearanceSettings + AppearancePanelWidth;
                await AnimateAppearancePanelAsync(-AppearancePanelWidth, 0);
                if (IsVisible)
                    UpdateEditorCanvasSize(EditorCanvasViewport.Bounds.Size);
            }
        }
        finally
        {
            _appearanceSettingsTransitioning = false;
            if (IsVisible) AppearanceSettingsButton.IsEnabled = true;
        }
    }

    private async Task AnimateAppearancePanelAsync(double from, double to)
    {
        const double durationMilliseconds = 220;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed.TotalMilliseconds < durationMilliseconds)
        {
            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / durationMilliseconds, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            AppearancePanelTransform.X = from + ((to - from) * eased);
            await Task.Delay(16);
        }
        AppearancePanelTransform.X = to;
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
        if (_dragBlock is null || sender is not Control control || !ReferenceEquals(e.Pointer.Captured, sender)) return;
        var point = e.GetPosition(EditorBlockCanvas);
        var canvasWidth = EditorBlockCanvas.Bounds.Width;
        var canvasHeight = EditorBlockCanvas.Bounds.Height;
        var maxX = Math.Max(0, canvasWidth - control.Bounds.Width);
        var maxY = Math.Max(0, canvasHeight - control.Bounds.Height);
        var x = point.X - _dragOffset.X;
        var y = point.Y - _dragOffset.Y;
        var gridSpacing = EditorGrid.GridSpacing;
        if ((e.KeyModifiers & KeyModifiers.Shift) == 0 && gridSpacing > 0)
        {
            x = SnapWithinBounds(x, maxX, gridSpacing);
            y = SnapWithinBounds(y, maxY, gridSpacing);
        }
        else
        {
            x = Math.Clamp(x, 0, maxX);
            y = Math.Clamp(y, 0, maxY);
        }

        var normalizedXRange = Math.Max(0, canvasWidth - ViewModel.OverlayPreviewWidth);
        var normalizedYRange = Math.Max(0, canvasHeight - 90);
        ViewModel.ApplyOverlayBlockPosition(
            _dragBlock.Key,
            normalizedXRange > 0 ? x / normalizedXRange : 0,
            normalizedYRange > 0 ? y / normalizedYRange : 0);
        e.Handled = true;
    }

    private static double SnapWithinBounds(double value, double max, double spacing)
    {
        var lastGridPosition = Math.Floor(max / spacing) * spacing;
        var snapped = Math.Round(value / spacing, MidpointRounding.AwayFromZero) * spacing;
        return Math.Clamp(snapped, 0, lastGridPosition);
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
