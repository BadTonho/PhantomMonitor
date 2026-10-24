using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;
using Avalonia.VisualTree;

namespace PhantomMonitor.App.Views;

/// <summary>View for choosing and editing the content of one overlay block.</summary>
public partial class OverlayBlockContentDialog : Window
{
    private const double ReorderStartThreshold = 5;

    private IPointer? _reorderPointer;
    private OverlayBlockItemChoiceViewModel? _reorderItem;
    private Point _reorderStart;
    private bool _isReordering;

    private OverlayBlockContentDialogViewModel ViewModel => (OverlayBlockContentDialogViewModel)DataContext!;

    public IReadOnlyList<OverlayBlockItemConfig> Result => ViewModel.Result;
    public IReadOnlyList<OverlayMetricOverrideConfig> ResultMetricOverrides => ViewModel.ResultMetricOverrides;
    public IReadOnlyList<OverlayGraph> ResultGraphs => ViewModel.ResultGraphs;
    public IReadOnlyList<OverlayTemplate> ResultTemplates => ViewModel.ResultTemplates;

    public OverlayBlockContentDialog()
    {
        InitializeComponent();
        InitializeContentReordering();
    }

    public OverlayBlockContentDialog(MainWindowViewModel viewModel, OverlayCanvasBlockViewModel block)
    {
        InitializeComponent();
        InitializeContentReordering();
        DataContext = new OverlayBlockContentDialogViewModel(viewModel, block);
    }

    private void OnAddSeriesPatternClick(object? sender, RoutedEventArgs e) => ViewModel.AddSeriesPattern();
    private void OnToggleSeriesCategoryClick(object? sender, RoutedEventArgs e) => ViewModel.ToggleVisibleSeriesSelection();
    private void OnMoveContentUpClick(object? sender, RoutedEventArgs e) => ViewModel.MoveSelectedContent(-1);
    private void OnMoveContentDownClick(object? sender, RoutedEventArgs e) => ViewModel.MoveSelectedContent(1);
    private void OnRemoveContentClick(object? sender, RoutedEventArgs e) => ViewModel.RemoveSelectedContent();

    private void InitializeContentReordering()
    {
        var list = this.FindControl<ListBox>("ContentItemsList");
        if (list is null) return;
        list.AddHandler(InputElement.PointerPressedEvent, OnContentListPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(InputElement.PointerMovedEvent, OnContentListPointerMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerReleasedEvent, OnContentListPointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerCaptureLostEvent, OnContentListPointerCaptureLost,
            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnContentListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list
            || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed
            || e.Source is not Control { DataContext: OverlayBlockItemChoiceViewModel item })
            return;

        ViewModel.SelectedContentItem = item;
        _reorderPointer = e.Pointer;
        _reorderItem = item;
        _reorderStart = e.GetPosition(list);
        _isReordering = false;
        e.Pointer.Capture(list);
    }

    private void OnContentListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ListBox list || _reorderPointer != e.Pointer || _reorderItem is null)
            return;

        var currentPosition = e.GetPosition(list);
        if (!_isReordering)
        {
            var deltaX = currentPosition.X - _reorderStart.X;
            var deltaY = currentPosition.Y - _reorderStart.Y;
            if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed
                || Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < ReorderStartThreshold)
                return;
            _isReordering = true;
        }

        if (currentPosition.X < 0 || currentPosition.X > list.Bounds.Width
            || currentPosition.Y < 0 || currentPosition.Y > list.Bounds.Height)
            return;

        var insertionIndex = FindInsertionIndex(list, currentPosition.Y);
        var sourceIndex = ViewModel.OrderedContentItems.IndexOf(_reorderItem);
        if (sourceIndex < 0 || insertionIndex < 0) return;
        if (sourceIndex < insertionIndex) insertionIndex--;
        var targetIndex = Math.Clamp(insertionIndex, 0, ViewModel.OrderedContentItems.Count - 1);
        ViewModel.MoveContent(sourceIndex, targetIndex);
    }

    private static int FindInsertionIndex(ListBox list, double pointerY)
    {
        var insertionIndex = -1;
        for (var index = 0; index < list.ItemCount; index++)
        {
            if (list.ContainerFromIndex(index) is not Control container) continue;
            var position = container.TranslatePoint(new Point(0, 0), list);
            if (position is null) continue;
            insertionIndex = index + 1;
            if (pointerY < position.Value.Y + container.Bounds.Height / 2)
                return index;
        }
        return insertionIndex;
    }

    private void OnContentListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_reorderPointer == e.Pointer)
            ClearContentReordering(releaseCapture: true);
    }

    private void OnContentListPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => ClearContentReordering(releaseCapture: false);

    private void ClearContentReordering(bool releaseCapture)
    {
        var pointer = _reorderPointer;
        _reorderPointer = null;
        _reorderItem = null;
        _isReordering = false;
        if (releaseCapture && pointer is not null)
            pointer.Capture(null);
    }

    private void OnSaveMetricOverrideClick(object? sender, RoutedEventArgs e) => ViewModel.SaveMetricOverride();
    private void OnMetricColorSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex }) ViewModel.SetMetricColor(hex);
    }
    private void OnResetMetricColorClick(object? sender, RoutedEventArgs e) => ViewModel.ResetMetricColor();
    private async void OnOpenContentPickerClick(object? sender, RoutedEventArgs e)
    {
        var pickerViewModel = ViewModel.CreateContentPickerDraft();
        var picker = new OverlayBlockContentPickerDialog(pickerViewModel);
        if (await picker.ShowDialog<bool>(this))
            ViewModel.CommitContentPickerDraft(pickerViewModel);
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.Apply();
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
