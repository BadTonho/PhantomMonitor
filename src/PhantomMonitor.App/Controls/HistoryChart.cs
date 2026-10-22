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

        var pen = GetOrCreatePen();
        if (pen is null) return;

        var width = Bounds.Width;
        var height = Bounds.Height;
        var usableHeight = height - 4;
        var denominator = values.Count - 1;

        // Única passada: Min() e Max() percorriam a coleção duas vezes por frame.
        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var value in values)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }
        var range = Math.Max(max - min, 1);

        // Segmentos desenhados em sequência sem materializar array de pontos
        // (antes: Select(...).ToArray() com closure a cada frame de render).
        var hasLast = false;
        var last = default(Point);
        for (var index = 0; index < values.Count; index++)
        {
            var point = new Point(
                index * width / denominator,
                height - 2 - ((values[index] - min) / range * usableHeight));
            if (hasLast)
                context.DrawLine(pen, last, point);
            else
                hasLast = true;
            last = point;
        }
    }

    private SolidColorBrush? _brush;
    private Pen? _pen;
    private Color _penColor;

    /// <summary>Pen/brush reusados entre frames: criar um par por render (até 10x/s
    /// por gráfico, 5 gráficos) só alimentava o GC.</summary>
    private Pen? GetOrCreatePen()
    {
        var color = Stroke;
        if (_pen is null || _brush is null || !_penColor.Equals(color))
        {
            _brush = new SolidColorBrush(color);
            _pen = new Pen(_brush, 2);
            _penColor = color;
        }
        return _pen;
    }
}
