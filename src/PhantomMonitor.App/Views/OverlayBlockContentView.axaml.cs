using Avalonia.Controls;
using Avalonia.Input;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public sealed partial class OverlayBlockContentView : UserControl
{
    public OverlayBlockContentView()
    {
        InitializeComponent();
    }

    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not OverlayCanvasBlockViewModel { IsPreview: false }
            || OperatingSystem.IsWindows()
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || TopLevel.GetTopLevel(this) is not Window window)
            return;

        window.BeginMoveDrag(e);
        e.Handled = true;
    }
}
