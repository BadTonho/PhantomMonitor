using Avalonia.Controls;
using Avalonia.Input;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public sealed partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            // Windows: clique-through + topmost + follow (perfis). Linux: nada —
            // janela arrastável/clicável com Topmost fixo, como sempre.
            if (OperatingSystem.IsWindows())
                InitializeWindowsServices();
        };
        Closed += (_, _) => ShutdownWindowsServices();
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
