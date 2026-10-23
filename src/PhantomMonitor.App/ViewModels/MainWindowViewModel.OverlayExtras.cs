using System.Collections.ObjectModel;
using Avalonia.Media;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

// Compila separadamente o rascunho de prévia e as definições aplicadas no OSD.
// Assim, alterações no editor só chegam ao overlay após salvar; as séries continuam
// recebendo amostras ao vivo nos dois conjuntos.
public sealed partial class MainWindowViewModel
{
    public ObservableCollection<object> OverlayExtraRows => _overlayExtraRows;
    public ObservableCollection<object> OverlayAppliedExtraRows => _overlayAppliedExtraRows;
    private readonly ObservableCollection<object> _overlayExtraRows = new();

    private readonly List<OverlayGraphRuntime> _overlayGraphs = [];
    private readonly List<OverlayTemplateRuntime> _overlayTemplates = [];
    private readonly List<OverlayGraphRuntime> _overlayAppliedGraphs = [];
    private readonly List<OverlayTemplateRuntime> _overlayAppliedTemplates = [];
    private string _overlayGraphConfigHash = "";

    private sealed class OverlayGraphRuntime
    {
        public required string Identity { get; init; }
        public required FrametimeBuffer Buffer { get; init; }
        public required OverlayGraphRowViewModel Row { get; init; }
        public required OverlayPlaceholderParser.Segment Segment { get; init; }
        public required string? ColorHex { get; init; }
    }

    private sealed class OverlayTemplateRuntime
    {
        public required string Identity { get; init; }
        public required OverlayTemplateRowViewModel Row { get; init; }
        public required IReadOnlyList<OverlayPlaceholderParser.Segment> Segments { get; init; }
        public required string? ColorHex { get; init; }
    }

    /// <summary>Compila graphs/templates do config (uma vez por arranque do VM).</summary>
    private void BuildOverlayExtraRows()
    {
        BuildOverlayExtraRows(_overlayGraphsDraft, _overlayTemplatesDraft,
            _overlayExtraRows, _overlayGraphs, _overlayTemplates);
        _overlayGraphConfigHash = ComputeOverlayExtrasHash(_overlayGraphsDraft, _overlayTemplatesDraft);
    }

    private void BuildOverlayAppliedExtraRows()
    {
        BuildOverlayExtraRows(_config.Overlay.Graphs, _config.Overlay.Templates,
            _overlayAppliedExtraRows, _overlayAppliedGraphs, _overlayAppliedTemplates);
    }

    private void BuildOverlayExtraRows(IEnumerable<OverlayGraph> graphs, IEnumerable<OverlayTemplate> templates,
        ObservableCollection<object> rows, List<OverlayGraphRuntime> graphRuntimes,
        List<OverlayTemplateRuntime> templateRuntimes)
    {
        rows.Clear();
        graphRuntimes.Clear();
        templateRuntimes.Clear();

        foreach (var graph in graphs)
        {
            // Resolve como placeholders de template (canônica → 1ª instância,
            // wildcard "{GpuTemperature#*}", nome amigável).
            var segments = OverlayPlaceholderParser.Compile($"{{{graph.Series}}}");
            var segment = segments.FirstOrDefault(s => !s.IsLiteral);
            if (segment is null) continue;

            var row = new OverlayGraphRowViewModel($"#graph:{graph.Id}")
            {
                MinY = graph.Min,
                MaxY = graph.Max,
                Height = Math.Clamp(graph.Height, 8, 120),
            };
            graphRuntimes.Add(new OverlayGraphRuntime
            {
                Identity = row.Identity,
                Buffer = new FrametimeBuffer(Math.Clamp(graph.Window, 30, 1200)),
                Row = row,
                Segment = segment,
                ColorHex = graph.Color,
            });
            rows.Add(row);
        }

        foreach (var template in templates)
        {
            var segments = OverlayPlaceholderParser.Compile(template.Text);
            if (segments.Count == 0) continue;

            var row = new OverlayTemplateRowViewModel($"#template:{template.Id}");
            templateRuntimes.Add(new OverlayTemplateRuntime
            {
                Identity = row.Identity,
                Row = row,
                Segments = segments,
                ColorHex = template.Color,
            });
            rows.Add(row);
        }

        RefreshOverlayGraphStrokes(graphRuntimes, templateRuntimes);
    }

    /// <summary>
    /// Config de graphs/templates pode ter mudado por fora (editar config.json e
    /// reiniciar/recarregar chama de novo): hash barato evita reconstruir a cada tick.
    /// </summary>
    public void EnsureOverlayExtrasCurrent()
    {
        var hash = ComputeOverlayExtrasHash(_overlayGraphsDraft, _overlayTemplatesDraft);
        if (hash != _overlayGraphConfigHash)
            BuildOverlayExtraRows();
    }

    private void UpdateOverlayAppliedExtraRows(SensorData? data)
        => UpdateOverlayExtraRows(data, _overlayAppliedGraphs, _overlayAppliedTemplates);

    private static string ComputeOverlayExtrasHash(IEnumerable<OverlayGraph>? graphs, IEnumerable<OverlayTemplate>? templates) =>
        string.Join("|", (graphs ?? []).Select(g => $"{g.Id};{g.Series};{g.Height:0.#};{g.Window};{g.Min};{g.Max};{g.Color}")
            .Concat((templates ?? []).Select(t => $"{t.Id};{t.Text};{t.Color}")));

    private void UpdateOverlayExtraRows(SensorData? data)
        => UpdateOverlayExtraRows(data, _overlayGraphs, _overlayTemplates);

    private static void UpdateOverlayExtraRows(SensorData? data, IReadOnlyList<OverlayGraphRuntime> graphs,
        IReadOnlyList<OverlayTemplateRuntime> templates)
    {
        if (data is null || (graphs.Count == 0 && templates.Count == 0)) return;

        foreach (var graph in graphs)
        {
            if (!OverlayPlaceholderParser.TryResolveValue(data, graph.Segment, out var value))
            {
                // Série ausente esconde a sparkline (buffer fica, como no WPF).
                graph.Row.IsVisible = false;
                continue;
            }

            graph.Row.IsVisible = true;
            graph.Buffer.Add((float)value);
            graph.Row.Samples = graph.Buffer.ToArray().Select(x => (double)x).ToArray();
        }

        foreach (var template in templates)
        {
            if (!OverlayPlaceholderParser.TryRender(data, template.Segments, out var text))
            {
                template.Row.IsVisible = false;
                continue;
            }

            template.Row.IsVisible = true;
            template.Row.Text = text;
        }
    }

    /// <summary>Redesenha as strokes dos gráficos (acento do config/perfil mudou).</summary>
    private void RefreshOverlayGraphStrokes()
    {
        RefreshOverlayGraphStrokes(_overlayGraphs, _overlayTemplates);
        RefreshOverlayGraphStrokes(_overlayAppliedGraphs, _overlayAppliedTemplates);
    }

    private void RefreshOverlayGraphStrokes(IReadOnlyList<OverlayGraphRuntime> graphs,
        IReadOnlyList<OverlayTemplateRuntime> templates)
    {
        foreach (var graph in graphs)
            graph.Row.Stroke = ResolveOverlayLineBrush(graph.ColorHex);
        foreach (var template in templates)
            template.Row.TextBrush = template.ColorHex is { Length: > 0 } hex
                ? OverlayBrushHelper.Resolve(hex, ResolveBrush("TextPrimaryBrush"))
                : ResolveBrush("TextPrimaryBrush");
    }

    /// <summary>Cor por linha: hex do config vence; vazio/inválido = acento vigente do OSD.</summary>
    private IBrush ResolveOverlayLineBrush(string? colorHex) =>
        OverlayBrushHelper.TryParseColor(colorHex, out var color)
            ? OverlayBrushHelper.Resolve(colorHex!, OverlayAccentBrush)
            : OverlayAccentBrush;
}
