using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

/// <summary>View for choosing and editing the content of one overlay block.</summary>
public partial class OverlayBlockContentDialog : Window
{
    private OverlayBlockContentDialogViewModel ViewModel => (OverlayBlockContentDialogViewModel)DataContext!;

    public IReadOnlyList<OverlayBlockItemConfig> Result => ViewModel.Result;
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
    private void OnNewGraphClick(object? sender, RoutedEventArgs e) => ViewModel.NewGraph();
    private void OnSaveGraphClick(object? sender, RoutedEventArgs e) => ViewModel.SaveGraph();
    private void OnRemoveGraphClick(object? sender, RoutedEventArgs e) => ViewModel.RemoveGraph();
    private void OnNewTemplateClick(object? sender, RoutedEventArgs e) => ViewModel.NewTemplate();
    private void OnSaveTemplateClick(object? sender, RoutedEventArgs e) => ViewModel.SaveTemplate();
    private void OnRemoveTemplateClick(object? sender, RoutedEventArgs e) => ViewModel.RemoveTemplate();

    private void OnEditGraphClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OverlayBlockContentChoiceViewModel choice })
            ViewModel.EditGraph(choice.Value);
    }

    private void OnEditTemplateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OverlayBlockContentChoiceViewModel choice })
            ViewModel.EditTemplate(choice.Value);
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.Apply();
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
