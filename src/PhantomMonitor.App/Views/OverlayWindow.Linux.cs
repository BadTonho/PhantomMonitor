using Avalonia;
using Avalonia.Controls;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public sealed partial class OverlayWindow
{
    private readonly Dictionary<string, PixelPoint> _linuxRequestedPositions = new(StringComparer.OrdinalIgnoreCase);
    private bool _positioningLinuxWindows;

    private void PositionWindowsOnPrimaryScreen()
    {
        if (_positioningLinuxWindows) return;
        var referenceWindow = _windows.Values.FirstOrDefault();
        var screen = referenceWindow?.Screens.Primary;
        if (screen is null) return;
        _positioningLinuxWindows = true;
        try
        {
            var scale = double.IsFinite(screen.Scaling) && screen.Scaling > 0 ? screen.Scaling : 1;
            var bounds = screen.Bounds;
            var width = bounds.Width / scale;
            var height = bounds.Height / scale;
            _viewModel.SetOverlayCanvasSize(width, height);

            foreach (var (key, window) in _windows)
            {
                if (!window.IsVisible) continue;
                var block = (OverlayCanvasBlockViewModel)window.DataContext!;
                var measuredWidth = window.Bounds.Width > 0 ? window.Bounds.Width : window.Width;
                var measuredHeight = window.Bounds.Height > 0 ? window.Bounds.Height : window.Height;
                var requested = new PixelPoint(
                    OverlayPositionMath.ToPixelCoordinate(block.X, bounds.X, bounds.Width, measuredWidth, scale),
                    OverlayPositionMath.ToPixelCoordinate(block.Y, bounds.Y, bounds.Height, measuredHeight, scale));
                _linuxRequestedPositions[key] = requested;
                window.Position = requested;
            }
        }
        finally
        {
            _positioningLinuxWindows = false;
        }
    }

    private PixelPoint? GetLinuxRequestedPosition(string key)
        => _linuxRequestedPositions.TryGetValue(key, out var position) ? position : null;
}
