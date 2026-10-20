using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PhantomMonitor.App.Controls;

public sealed class HistoryChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<HistoryChart, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<Color> StrokeProperty =
        AvaloniaProperty.Register<HistoryChart, Color>(nameof(Stroke), Color.Parse("#38BDF8"));

    static HistoryChart()
    {
        AffectsRender<HistoryChart>(ValuesProperty, StrokeProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var values = Values;
        if (values is not { Count: > 1 } || Bounds.Width <= 1 || Bounds.Height <= 1) return;

        var min = values.Min();
        var max = values.Max();
        var range = Math.Max(max - min, 1);
        var pen = new Pen(new SolidColorBrush(Stroke), 2);
        var points = values.Select((value, index) => new Point(
            index * Bounds.Width / (values.Count - 1),
            Bounds.Height - 2 - ((value - min) / range * (Bounds.Height - 4)))).ToArray();

        for (var i = 1; i < points.Length; i++)
            context.DrawLine(pen, points[i - 1], points[i]);
    }
}
