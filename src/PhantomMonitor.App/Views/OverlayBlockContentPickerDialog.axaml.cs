using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

/// <summary>Modal picker for adding categories, series, graphs, and templates to an overlay block.</summary>
public partial class OverlayBlockContentPickerDialog : Window
{
    private OverlayBlockContentDialogViewModel ViewModel => (OverlayBlockContentDialogViewModel)DataContext!;

    public OverlayBlockContentPickerDialog() => InitializeComponent();

    public OverlayBlockContentPickerDialog(OverlayBlockContentDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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

    private void OnAddClick(object? sender, RoutedEventArgs e)
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
