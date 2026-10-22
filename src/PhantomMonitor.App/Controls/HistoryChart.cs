using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PhantomMonitor.App.Controls;

public sealed class HistoryChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<HistoryChart, IReadOnlyList<double>?>(nameof(Values));

    /// <summary>Cor da linha. IBrush (e não Color) para os tokens de tema
    /// serem consumíveis via {DynamicResource} direto do XAML.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<HistoryChart, IBrush?>(nameof(Stroke));

    /// <summary>Piso do eixo Y (opcional; nulo = auto pelo intervalo dos dados).</summary>
    public static readonly StyledProperty<double?> MinYProperty =
        AvaloniaProperty.Register<HistoryChart, double?>(nameof(MinY));

    /// <summary>Teto do eixo Y (opcional; nulo = auto pelo intervalo dos dados).</summary>
    public static readonly StyledProperty<double?> MaxYProperty =
        AvaloniaProperty.Register<HistoryChart, double?>(nameof(MaxY));

    /// <summary>
    /// Piso do teto automático, no estilo do Minigraph do WPF: o topo do eixo é
    /// no mínimo esse valor (e cresce além dele quando os dados passarem).
    /// Com MaxY definido, o teto fixo vence. Ex.: 33.3 ms no frametime.
    /// </summary>
    public static readonly StyledProperty<double?> MaxFloorProperty =
        AvaloniaProperty.Register<HistoryChart, double?>(nameof(MaxFloor));

    /// <summary>Valor de uma linha-guia horizontal tracejada (opcional; ex.: 16.66 ms = 60 FPS).</summary>
    public static readonly StyledProperty<double?> GuideAtProperty =
        AvaloniaProperty.Register<HistoryChart, double?>(nameof(GuideAt));

    /// <summary>Cor da linha-guia (token do tema via XAML).</summary>
    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<HistoryChart, IBrush?>(nameof(GuideBrush));

    static HistoryChart()
    {
        AffectsRender<HistoryChart>(ValuesProperty, StrokeProperty, MinYProperty, MaxYProperty,
            MaxFloorProperty, GuideAtProperty, GuideBrushProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double? MinY
    {
        get => GetValue(MinYProperty);
        set => SetValue(MinYProperty, value);
    }

    public double? MaxY
    {
        get => GetValue(MaxYProperty);
        set => SetValue(MaxYProperty, value);
    }

    public double? MaxFloor
    {
        get => GetValue(MaxFloorProperty);
        set => SetValue(MaxFloorProperty, value);
    }

    public double? GuideAt
    {
        get => GetValue(GuideAtProperty);
        set => SetValue(GuideAtProperty, value);
    }

    public IBrush? GuideBrush
    {
        get => GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
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

        // Eixo fixo (MinY/MaxY) vence; MaxFloor pisca o teto automático; sem ambos, auto.
        if (MinY is { } minFixed) min = minFixed;
        if (MaxY is { } maxFixed) max = maxFixed;
        else if (MaxFloor is { } floor) max = Math.Max(max, floor);

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

        // Linha-guia de valor (ex.: 60 FPS no frametime), tracejada e sutil.
        if (GuideAt is { } guide && guide >= min && guide <= max)
        {
            var guidePen = GetOrCreateGuidePen();
            if (guidePen is not null)
            {
                var y = height - 2 - ((guide - min) / range * usableHeight);
                context.DrawLine(guidePen, new Point(0, y), new Point(width, y));
            }
        }
    }

    private Pen? _pen;
    private IBrush? _penBrush;
    private Pen? _guidePen;
    private IBrush? _guidePenBrush;

    /// <summary>Pen reusada entre frames: criar um par por render (até 10x/s
    /// por gráfico, 5 gráficos) só alimentava o GC. Referência trocada
    /// (ex.: troca de tema via DynamicResource) recria o pen.</summary>
    private Pen? GetOrCreatePen()
    {
        var brush = Stroke;
        if (brush is null) return null;

        if (_pen is null || !ReferenceEquals(_penBrush, brush))
        {
            _pen = new Pen(brush, 2);
            _penBrush = brush;
        }
        return _pen;
    }

    private Pen? GetOrCreateGuidePen()
    {
        var brush = GuideBrush;
        if (brush is null) return null;

        if (_guidePen is null || !ReferenceEquals(_guidePenBrush, brush))
        {
            _guidePen = new Pen(brush, 1, new DashStyle(DashStyle.Dash.Dashes, 2));
            _guidePenBrush = brush;
        }
        return _guidePen;
    }
}
