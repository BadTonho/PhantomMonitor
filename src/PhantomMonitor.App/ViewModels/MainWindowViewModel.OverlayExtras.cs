using System.Collections.ObjectModel;
using Avalonia.Media;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

// Partes "estáticas por config" do OSD que o WPF mantinha no code-behind da
// janela (P6.6 templates + P8.5 gráficos por série): compiladas uma única vez —
// os dois campos só mudam por edição manual do config.json (recarregar exige
// reiniciar o app, mesma política do WPF, cuja recriação do overlay faz isso).
// As amostras avançam a cada snapshot via UpdateOverlayExtraRows (chamado por
// UpdateOverlayRows) e as rows são reconciliadas por identidade.
public sealed partial class MainWindowViewModel
{
    public ObservableCollection<object> OverlayExtraRows => _overlayExtraRows;
    private readonly ObservableCollection<object> _overlayExtraRows = new();

    private readonly List<OverlayGraphRuntime> _overlayGraphs = [];
    private readonly List<OverlayTemplateRuntime> _overlayTemplates = [];
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
        var graphs = _config.Overlay.Graphs ?? [];
        var templates = _config.Overlay.Templates ?? [];

        _overlayExtraRows.Clear();
        _overlayGraphs.Clear();
        _overlayTemplates.Clear();
        _overlayGraphConfigHash = ComputeOverlayExtrasHash(graphs, templates);

        foreach (var graph in graphs)
        {
            // Resolve como placeholders de template (canônica → 1ª instância,
            // wildcard "{GpuTemperature#*}", nome amigável).
            var segments = OverlayPlaceholderParser.Compile($"{{{graph.Series}}}");
            var segment = segments.FirstOrDefault(s => !s.IsLiteral);
            if (segment is null) continue;

            var row = new OverlayGraphRowViewModel($"#graph:{graph.Series}")
            {
                MinY = graph.Min,
                MaxY = graph.Max,
                Height = Math.Clamp(graph.Height, 8, 120),
            };
            _overlayGraphs.Add(new OverlayGraphRuntime
            {
                Identity = row.Identity,
                Buffer = new FrametimeBuffer(Math.Clamp(graph.Window, 30, 1200)),
                Row = row,
                Segment = segment,
                ColorHex = graph.Color,
            });
            _overlayExtraRows.Add(row);
        }

        foreach (var template in templates)
        {
            var segments = OverlayPlaceholderParser.Compile(template.Text);
            if (segments.Count == 0) continue;

            var row = new OverlayTemplateRowViewModel($"#template:{template.Text}");
            _overlayTemplates.Add(new OverlayTemplateRuntime
            {
                Identity = row.Identity,
                Row = row,
                Segments = segments,
                ColorHex = template.Color,
            });
            _overlayExtraRows.Add(row);
        }

        RefreshOverlayGraphStrokes();
    }

    /// <summary>
    /// Config de graphs/templates pode ter mudado por fora (editar config.json e
    /// reiniciar/recarregar chama de novo): hash barato evita reconstruir a cada tick.
    /// </summary>
    public void EnsureOverlayExtrasCurrent()
    {
        var hash = ComputeOverlayExtrasHash(_config.Overlay.Graphs ?? [], _config.Overlay.Templates ?? []);
        if (hash != _overlayGraphConfigHash)
            BuildOverlayExtraRows();
    }

    private static string ComputeOverlayExtrasHash(IEnumerable<OverlayGraph>? graphs, IEnumerable<OverlayTemplate>? templates) =>
        string.Join("|", (graphs ?? []).Select(g => $"{g.Series};{g.Height:0.#};{g.Window};{g.Min};{g.Max};{g.Color}")
            .Concat((templates ?? []).Select(t => $"{t.Text};{t.Color}")));

    private void UpdateOverlayExtraRows(SensorData? data)
    {
        if (data is null || (_overlayGraphs.Count == 0 && _overlayTemplates.Count == 0)) return;

        foreach (var graph in _overlayGraphs)
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

        foreach (var template in _overlayTemplates)
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
        foreach (var graph in _overlayGraphs)
            graph.Row.Stroke = ResolveOverlayLineBrush(graph.ColorHex);
        foreach (var template in _overlayTemplates)
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
