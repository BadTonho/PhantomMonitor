using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public partial class OverlayAppearanceControls : UserControl
{
    public OverlayAppearanceControls() => InitializeComponent();

    private void OnBackgroundColorSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex } && DataContext is MainWindowViewModel viewModel)
            viewModel.OverlayBackgroundColorText = hex;
    }

    private void OnResetBackgroundColorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.OverlayBackgroundColorText = "";
    }

    private void OnBorderColorSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex } && DataContext is MainWindowViewModel viewModel)
            viewModel.OverlayBorderColorText = hex;
    }

    private void OnResetBorderColorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.OverlayBorderColorText = "";
    }
}
