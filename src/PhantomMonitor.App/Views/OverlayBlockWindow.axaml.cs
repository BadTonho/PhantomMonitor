using Avalonia.Controls;
using Avalonia.Input;

namespace PhantomMonitor.App.Views;

public sealed partial class OverlayBlockWindow : Window
{
    public OverlayBlockWindow()
    {
        InitializeComponent();
    }

    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
