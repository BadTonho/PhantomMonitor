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
    private OverlayBlockOrderRowViewModel? _reorderItem;
    private Point _reorderStart;
    private Point _dragGrabOffset;
    private bool _isReordering;
    private bool _dragOverList;
    private int _pendingInsertionIndex = -1;

    private OverlayBlockContentDialogViewModel ViewModel => (OverlayBlockContentDialogViewModel)DataContext!;

    public IReadOnlyList<OverlayBlockItemConfig> Result => ViewModel.Result;
    public IReadOnlyList<OverlayMetricOverrideConfig> ResultMetricOverrides => ViewModel.ResultMetricOverrides;
    public IReadOnlyDictionary<string, List<string>> ResultMetricOrder => ViewModel.ResultMetricOrder;
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
        if (sender is not ListBox list || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed
            || FindOrderRow(e.Source, list) is not { } item)
            return;

        ViewModel.SelectedOrderRow = item;
        _reorderPointer = e.Pointer;
        _reorderItem = item;
        _reorderStart = e.GetPosition(list);
        _dragGrabOffset = list.ContainerFromItem(item) is Control container
            ? e.GetPosition(container)
            : e.GetPosition(list);
        _isReordering = false;
        _dragOverList = false;
        _pendingInsertionIndex = -1;
        InsertionMarker.IsVisible = false;
        e.Pointer.Capture(list);
        e.Handled = true;
    }

    private void OnContentListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ListBox list || _reorderPointer != e.Pointer || _reorderItem is null)
            return;

        _reorderItem = ViewModel.FindCurrentOrderRow(_reorderItem);
        if (_reorderItem is null)
        {
            ClearContentReordering(releaseCapture: true);
            return;
        }

        var currentPosition = e.GetPosition(list);
        if (!_isReordering)
        {
            var deltaX = currentPosition.X - _reorderStart.X;
            var deltaY = currentPosition.Y - _reorderStart.Y;
            if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed
                || Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < ReorderStartThreshold)
                return;
            _isReordering = true;
            DragGhostKind.Text = _reorderItem.Kind;
            DragGhostLabel.Text = _reorderItem.Label;
            if (list.ContainerFromItem(_reorderItem) is Control draggedContainer)
            {
                DragGhost.Width = draggedContainer.Bounds.Width;
                DragGhost.Height = draggedContainer.Bounds.Height;
            }
            DragGhost.IsVisible = true;
        }

        var ghostPosition = e.GetPosition(DragOverlay);
        Canvas.SetLeft(DragGhost, Math.Max(0, ghostPosition.X - _dragGrabOffset.X));
        Canvas.SetTop(DragGhost, Math.Max(0, ghostPosition.Y - _dragGrabOffset.Y));

        _dragOverList = currentPosition.X >= 0 && currentPosition.X <= list.Bounds.Width
            && currentPosition.Y >= 0 && currentPosition.Y <= list.Bounds.Height;
        if (!_dragOverList)
        {
            InsertionMarker.IsVisible = false;
            return;
        }

        var insertionIndex = FindInsertionIndex(list, currentPosition.Y);
        if (insertionIndex < 0) return;
        _pendingInsertionIndex = ConstrainInsertionIndex(list, _reorderItem, insertionIndex, currentPosition.Y);
        PositionInsertionMarker(list, _pendingInsertionIndex);
    }

    private static OverlayBlockOrderRowViewModel? FindOrderRow(object? source, Control boundary)
    {
        if (source is not Control currentControl) return null;
        Control? current = currentControl;
        while (current is not null)
        {
            if (current.DataContext is OverlayBlockOrderRowViewModel row)
                return row;
            if (ReferenceEquals(current, boundary)) break;
            current = current.Parent as Control;
        }
        return null;
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

    private int ConstrainInsertionIndex(ListBox list, OverlayBlockOrderRowViewModel item, int insertionIndex, double pointerY)
    {
        if (item.IsMetric)
        {
            var siblingIndices = ViewModel.OrderRows
                .Select((row, index) => (row, index))
                .Where(entry => entry.row.IsMetric && entry.row.GroupKey.Equals(item.GroupKey, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.index)
                .ToArray();
            if (siblingIndices.Length == 0) return insertionIndex;
            return Math.Clamp(insertionIndex, siblingIndices[0], siblingIndices[^1] + 1);
        }

        var groupInsertionSlots = ViewModel.OrderRows
            .Select((row, index) => (row, index))
            .Where(entry => !entry.row.IsMetric)
            .Select(entry => entry.index)
            .Append(ViewModel.OrderRows.Count)
            .Distinct()
            .ToArray();
        return groupInsertionSlots
            .OrderBy(slot => Math.Abs(GetInsertionY(list, slot) - pointerY))
            .FirstOrDefault(insertionIndex);
    }

    private void PositionInsertionMarker(ListBox list, int insertionIndex)
    {
        var position = list.TranslatePoint(new Point(0, GetInsertionY(list, insertionIndex)), InsertionMarkerCanvas);
        if (position is null)
        {
            InsertionMarker.IsVisible = false;
            return;
        }

        Canvas.SetLeft(InsertionMarker, 0);
        Canvas.SetTop(InsertionMarker, position.Value.Y - InsertionMarker.Height / 2);
        InsertionMarker.Width = list.Bounds.Width;
        InsertionMarker.IsVisible = true;
    }

    private double GetInsertionY(ListBox list, int insertionIndex)
    {
        if (ViewModel.OrderRows.Count == 0) return 0;
        if (insertionIndex < ViewModel.OrderRows.Count
            && list.ContainerFromIndex(insertionIndex) is Control nextContainer)
        {
            var position = nextContainer.TranslatePoint(new Point(0, 0), list);
            if (position is not null) return position.Value.Y;
        }

        var lastIndex = Math.Min(ViewModel.OrderRows.Count - 1, list.ItemCount - 1);
        if (lastIndex >= 0 && list.ContainerFromIndex(lastIndex) is Control lastContainer)
        {
            var position = lastContainer.TranslatePoint(new Point(0, 0), list);
            if (position is not null) return position.Value.Y + lastContainer.Bounds.Height;
        }
        return list.Bounds.Height;
    }

    private void CommitContentReordering()
    {
        if (!_isReordering || !_dragOverList || _reorderItem is null || _pendingInsertionIndex < 0)
            return;
        var currentItem = ViewModel.FindCurrentOrderRow(_reorderItem);
        if (currentItem is null) return;
        var sourceIndex = ViewModel.OrderRows.IndexOf(currentItem);
        ViewModel.MoveOrderRowToInsertion(sourceIndex, _pendingInsertionIndex);
    }

    private void OnContentListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_reorderPointer == e.Pointer)
        {
            if (sender is ListBox list && _isReordering)
            {
                var position = e.GetPosition(list);
                _dragOverList = position.X >= 0 && position.X <= list.Bounds.Width
                    && position.Y >= 0 && position.Y <= list.Bounds.Height;
                if (_dragOverList)
                {
                    var insertionIndex = FindInsertionIndex(list, position.Y);
                    _pendingInsertionIndex = insertionIndex >= 0
                        ? ConstrainInsertionIndex(list, _reorderItem!, insertionIndex, position.Y)
                        : -1;
                }
                else
                    _pendingInsertionIndex = -1;
                CommitContentReordering();
            }
            ClearContentReordering(releaseCapture: true);
        }
    }

    private void OnContentListPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => ClearContentReordering(releaseCapture: false);

    private void ClearContentReordering(bool releaseCapture)
    {
        var pointer = _reorderPointer;
        _reorderPointer = null;
        _reorderItem = null;
        _isReordering = false;
        _dragOverList = false;
        _pendingInsertionIndex = -1;
        DragGhost.IsVisible = false;
        InsertionMarker.IsVisible = false;
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

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is OverlayBlockContentDialogViewModel viewModel)
            viewModel.Dispose();
        base.OnClosed(e);
    }
}
