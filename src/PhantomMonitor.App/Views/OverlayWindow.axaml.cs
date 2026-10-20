using Avalonia.Controls;
using Avalonia.Input;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public sealed partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
    }

    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnHideClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.HideOverlay();
        else
            Hide();
    }
}
