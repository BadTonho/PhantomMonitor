using System.Windows;
using System.Windows.Media;

namespace PhantomMonitor.Controls;

/// <summary>
/// Sparkline de desenho retido. Resolve o hotspot clássico do OSD: criar
/// Line/Polyline/PointCollection/DoubleCollection a cada tick dentro de um Canvas
/// (churn de visuais + layout). Aqui, mudar <see cref="Values"/> apenas invalida o
/// render, e a pipeline desenha eixo, guias e polyline com pens congelados.
///
/// Modos de eixo Y (mesma semântica dos caminhos antigos do overlay):
/// • Fixo: <see cref="MinY"/> e <see cref="MaxY"/> definidos.
/// • Extensões: <see cref="MaxFloor"/> definido e MinY nulo → janela 0..max(dados, floor)
///   (gráfico de frametime).
/// • Auto: nenhum fixo → min/máx da janela, com piso simétrico de ±0.5 para séries planas.
/// </summary>
public sealed class Minigraph : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<float>), typeof(Minigraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Minigraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnStrokeChanged));

    public static readonly DependencyProperty MinYProperty = DependencyProperty.Register(
        nameof(MinY), typeof(double?), typeof(Minigraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxYProperty = DependencyProperty.Register(
        nameof(MaxY), typeof(double?), typeof(Minigraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Piso do eixo no modo extensões (min=0); nulo desativa o modo.</summary>
    public static readonly DependencyProperty MaxFloorProperty = DependencyProperty.Register(
        nameof(MaxFloor), typeof(double?), typeof(Minigraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Linha guia tracejada no valor dado (ex.: 16.66 ms = 60 FPS).</summary>
    public static readonly DependencyProperty GuideAtProperty = DependencyProperty.Register(
        nameof(GuideAt), typeof(double?), typeof(Minigraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Guias tracejadas nos extremos (usadas pelas sparklines de série).</summary>
    public static readonly DependencyProperty EdgeGuidesProperty = DependencyProperty.Register(
        nameof(EdgeGuides), typeof(bool), typeof(Minigraph),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Descida em px da base da polyline (sparklines de série usam 1, como antes).</summary>
    public static readonly DependencyProperty TopShiftProperty = DependencyProperty.Register(
        nameof(TopShift), typeof(double), typeof(Minigraph),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void OnStrokeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Minigraph)d)._strokePen = null; // pen reconstruída no próximo render

    public IReadOnlyList<float>? Values
    {
        get => (IReadOnlyList<float>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double? MinY
    {
        get => (double?)GetValue(MinYProperty);
        set => SetValue(MinYProperty, value);
    }

    public double? MaxY
    {
        get => (double?)GetValue(MaxYProperty);
        set => SetValue(MaxYProperty, value);
    }

    public double? MaxFloor
    {
        get => (double?)GetValue(MaxFloorProperty);
        set => SetValue(MaxFloorProperty, value);
    }

    public double? GuideAt
    {
        get => (double?)GetValue(GuideAtProperty);
        set => SetValue(GuideAtProperty, value);
    }

    public bool EdgeGuides
    {
        get => (bool)GetValue(EdgeGuidesProperty);
        set => SetValue(EdgeGuidesProperty, value);
    }

    public double TopShift
    {
        get => (double)GetValue(TopShiftProperty);
        set => SetValue(TopShiftProperty, value);
    }

    // ---------- Pens/brush de guia (compartilhados, congelados) ----------

    private static Brush _guideBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
    private static Pen? _guidePen;
    private static Pen? _edgeGuidePen;

    /// <summary>Injeta a cor de guia da janela de overlay antes de criar gráficos
    /// (evita duplicar definição de cor entre control e consumidores).</summary>
    public static void SetGuideBrush(Brush brush)
    {
        _guideBrush = brush;
        _guidePen = null;
        _edgeGuidePen = null;
    }

    private static Pen GuidePen => _guidePen ??= BuildGuide(0.8);
    private static Pen EdgeGuidePen => _edgeGuidePen ??= BuildGuide(0.6);

    private static Pen BuildGuide(double thickness)
    {
        var pen = new Pen(_guideBrush, thickness)
        {
            DashStyle = new DashStyle([2.0, 2.0], 0),
        };
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private Pen? _strokePen;

    protected override void OnRender(DrawingContext drawingContext)
    {
        var values = Values;
        if (values is not { Count: > 1 }) return;

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Eixo Y nos três modos, mesma matemática dos predecessores.
        double lo, hi;
        if (MinY.HasValue && MaxY.HasValue)
        {
            lo = MinY.Value;
            hi = MaxY.Value;
        }
        else if (MaxFloor.HasValue)
        {
            var max = MaxFloor.Value;
            foreach (var v in values)
                if (v > max) max = v;
            lo = 0;
            hi = max;
        }
        else
        {
            lo = double.MaxValue;
            hi = double.MinValue;
            foreach (var v in values)
            {
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            if (hi - lo < 0.001)
            {
                // Série plana (todos os valores iguais): piso simétrico para não colapsar no meio do gráfico.
                lo -= 0.5;
                hi += 0.5;
            }
        }
        if (hi - lo <= 0) hi = lo + 1; // guarda final contra divisão por zero
        var span = hi - lo;
        var topShift = TopShift;

        if (GuideAt is double guide)
        {
            var y = h - ((guide - lo) / span) * (h - 2);
            if (y >= 0 && y <= h)
                drawingContext.DrawLine(GuidePen, new Point(0, y), new Point(w, y));
        }

        if (EdgeGuides)
        {
            foreach (var y in new[] { 0.5, h - 1.5 })
                drawingContext.DrawLine(EdgeGuidePen, new Point(0, y), new Point(w, y));
        }

        var stroke = Stroke;
        if (stroke is null) return;
        var pen = _strokePen;
        if (pen is null || !ReferenceEquals(pen.Brush, stroke))
        {
            pen = new Pen(stroke, 1.2) { LineJoin = PenLineJoin.Round };
            if (pen.CanFreeze) pen.Freeze();
            _strokePen = pen;
        }

        // Polyline como StreamGeometry congelado: um objeto visual por render, no
        // lugar de Canvas.Children.Clear + Line + Polyline + PointCollection novos.
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var count = values.Count;
            var denominator = Math.Max(count, 120);
            var started = false;
            for (var i = 0; i < count; i++)
            {
                double x = w - (count - i) * (w / denominator);
                double y = Math.Clamp(h - ((values[i] - lo) / span) * (h - 2) - topShift, 0, h);
                if (started)
                    context.LineTo(new Point(x, y), true, true);
                else
                {
                    context.BeginFigure(new Point(x, y), false, false);
                    started = true;
                }
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
