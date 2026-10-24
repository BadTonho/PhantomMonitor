using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Models;

namespace PhantomMonitor.App.Views;

public partial class ThemePaletteEditorWindow : Window
{
    private ThemePaletteEditorViewModel ViewModel => (ThemePaletteEditorViewModel)DataContext!;

    public ThemePaletteEditorWindow()
    {
        InitializeComponent();
    }

    public ThemePaletteEditorWindow(ThemePaletteEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnResetGroupClick(object? sender, RoutedEventArgs e) => ViewModel.ResetSelectedGroup();
    private void OnResetTokenClick(object? sender, RoutedEventArgs e) => ViewModel.ResetSelectedToken();
    private void OnResetAllClick(object? sender, RoutedEventArgs e) => ViewModel.ResetAll();
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close((ThemePaletteConfig?)null);
    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close(ViewModel.Draft.Clone());
}
