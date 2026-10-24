using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

/// <summary>View for choosing and editing the content of one overlay block.</summary>
public partial class OverlayBlockContentDialog : Window
{
    private static readonly DataFormat<string> ContentIndexDataFormat =
        DataFormat.CreateInProcessFormat<string>("PhantomMonitor.OverlayContentIndex");

    private OverlayBlockContentDialogViewModel ViewModel => (OverlayBlockContentDialogViewModel)DataContext!;

    public IReadOnlyList<OverlayBlockItemConfig> Result => ViewModel.Result;
    public IReadOnlyList<OverlayMetricOverrideConfig> ResultMetricOverrides => ViewModel.ResultMetricOverrides;
    public IReadOnlyList<OverlayGraph> ResultGraphs => ViewModel.ResultGraphs;
    public IReadOnlyList<OverlayTemplate> ResultTemplates => ViewModel.ResultTemplates;

    public OverlayBlockContentDialog() => InitializeComponent();

    public OverlayBlockContentDialog(MainWindowViewModel viewModel, OverlayCanvasBlockViewModel block)
    {
        InitializeComponent();
        DataContext = new OverlayBlockContentDialogViewModel(viewModel, block);
    }

    private void OnAddSeriesPatternClick(object? sender, RoutedEventArgs e) => ViewModel.AddSeriesPattern();
    private void OnToggleSeriesCategoryClick(object? sender, RoutedEventArgs e) => ViewModel.ToggleVisibleSeriesSelection();
    private void OnMoveContentUpClick(object? sender, RoutedEventArgs e) => ViewModel.MoveSelectedContent(-1);
    private void OnMoveContentDownClick(object? sender, RoutedEventArgs e) => ViewModel.MoveSelectedContent(1);
    private void OnRemoveContentClick(object? sender, RoutedEventArgs e) => ViewModel.RemoveSelectedContent();

    private async void OnContentItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: OverlayBlockItemChoiceViewModel item } row
            || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            return;

        var sourceIndex = ViewModel.OrderedContentItems.IndexOf(item);
        if (sourceIndex < 0) return;

        ViewModel.SelectedContentItem = item;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ContentIndexDataFormat, sourceIndex.ToString(CultureInfo.InvariantCulture)));
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }

    private void OnContentItemDragOver(object? sender, DragEventArgs e)
    {
        if (!TryGetDraggedIndex(e, out _)) return;
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnContentItemDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: OverlayBlockItemChoiceViewModel targetItem } target
            || !TryGetDraggedIndex(e, out var sourceIndex))
            return;

        var targetIndex = ViewModel.OrderedContentItems.IndexOf(targetItem);
        if (targetIndex < 0) return;

        var insertIndex = targetIndex + (e.GetPosition(target).Y >= target.Bounds.Height / 2 ? 1 : 0);
        if (sourceIndex < insertIndex) insertIndex--;
        insertIndex = Math.Clamp(insertIndex, 0, ViewModel.OrderedContentItems.Count - 1);
        ViewModel.MoveContent(sourceIndex, insertIndex);
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static bool TryGetDraggedIndex(DragEventArgs e, out int index)
        => int.TryParse(e.DataTransfer.TryGetValue(ContentIndexDataFormat), NumberStyles.None,
            CultureInfo.InvariantCulture, out index);

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
