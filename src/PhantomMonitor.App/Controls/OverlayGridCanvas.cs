using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PhantomMonitor.App.Controls;

public sealed class OverlayGridCanvas : Control
{
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<OverlayGridCanvas, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<double> GridSpacingProperty =
        AvaloniaProperty.Register<OverlayGridCanvas, double>(nameof(GridSpacing), 20);

    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public double GridSpacing { get => GetValue(GridSpacingProperty); set => SetValue(GridSpacingProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (LineBrush is null || GridSpacing <= 0) return;

        var bounds = new Rect(Bounds.Size);
        var pen = new Pen(LineBrush, 0.7);
        for (var x = 0d; x <= bounds.Width; x += GridSpacing)
            context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
        for (var y = 0d; y <= bounds.Height; y += GridSpacing)
            context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
    }
}
