using Avalonia;
using Avalonia.Controls;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public sealed partial class OverlayWindow
{
    private void PositionWindowsOnPrimaryScreen()
    {
        var referenceWindow = _windows.Values.FirstOrDefault();
        var screen = referenceWindow?.Screens.Primary;
        if (screen is null) return;
        var scale = screen.Scaling;
        var bounds = screen.Bounds;
        var width = bounds.Width / scale;
        var height = bounds.Height / scale;
        _viewModel.SetOverlayCanvasSize(width, height);

        foreach (var window in _windows.Values)
        {
            if (!window.IsVisible) continue;
            var block = (OverlayCanvasBlockViewModel)window.DataContext!;
            var left = bounds.X + (int)OverlayPositionMath.ToPosition(block.X, bounds.Width, window.Width * scale);
            var top = bounds.Y + (int)OverlayPositionMath.ToPosition(block.Y, bounds.Height, window.Height * scale);
            window.Position = new PixelPoint((int)left, (int)top);
        }
    }
}
