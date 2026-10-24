using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<string, OverlayFrametimeGraphViewModel> _frametimeGraphItems = new(StringComparer.OrdinalIgnoreCase);

    private void InitializeOverlayBlocks()
    {
        _overlayBlocks.Clear();
        _overlayAppliedBlocks.Clear();
        foreach (var config in _config.Overlay.Blocks ?? [])
        {
            var blockConfig = CloneOverlayBlock(config);
            var block = new OverlayCanvasBlockViewModel(blockConfig, this, SetOverlayBlockPosition);
            block.SetCanvasSize(OverlayEditorCanvasWidth, OverlayEditorCanvasHeight);
            _overlayBlocks.Add(block);
            var applied = new OverlayCanvasBlockViewModel(CloneOverlayBlock(blockConfig), this, static (_, _, _) => { });
            applied.SetCanvasSize(OverlayEditorCanvasWidth, OverlayEditorCanvasHeight);
            _overlayAppliedBlocks.Add(applied);
        }
    }

    private void SetOverlayBlockPosition(string id, double x, double y)
    {
        var block = _overlayBlocks.FirstOrDefault(item => item.Key.Equals(id, StringComparison.OrdinalIgnoreCase));
        block?.SetNormalizedPosition(x, y);
    }

    public void ApplyOverlayBlockPosition(string id, double x, double y) => SetOverlayBlockPosition(id, x, y);

    public OverlayCanvasBlockViewModel AddOverlayBlock(double x, double y)
    {
        var config = new OverlayBlockConfig { Id = Guid.NewGuid().ToString("N"), X = Math.Clamp(x, 0, 1), Y = Math.Clamp(y, 0, 1) };
        var block = new OverlayCanvasBlockViewModel(config, this, SetOverlayBlockPosition);
        block.SetCanvasSize(OverlayEditorCanvasWidth, OverlayEditorCanvasHeight);
        _overlayBlocks.Add(block);
        return block;
    }

    public void RemoveOverlayBlock(OverlayCanvasBlockViewModel block)
    {
        if (!_overlayBlocks.Remove(block)) return;
        UpdateOverlayRows(_latestSensorData);
    }

    public void SetOverlayBlockDefinition(OverlayCanvasBlockViewModel block, IEnumerable<OverlayBlockItemConfig> items,
        IEnumerable<OverlayGraph> graphs, IEnumerable<OverlayTemplate> templates)
    {
        block.Config.Items = items.Select(item => new OverlayBlockItemConfig { Kind = item.Kind, Value = item.Value }).ToList();
        _overlayGraphsDraft.Clear();
        foreach (var graph in graphs) _overlayGraphsDraft.Add(CloneOverlayGraph(graph));
        _overlayTemplatesDraft.Clear();
        foreach (var template in templates) _overlayTemplatesDraft.Add(CloneOverlayTemplate(template));
        var graphIds = _overlayGraphsDraft.Select(graph => graph.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var templateIds = _overlayTemplatesDraft.Select(template => template.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in _overlayBlocks)
            candidate.Config.Items.RemoveAll(item => item.Kind == "graph" && !graphIds.Contains(item.Value)
                || item.Kind == "template" && !templateIds.Contains(item.Value));
        EnsureOverlayExtrasCurrent();
        UpdateOverlayRows(_latestSensorData);
    }

    public void CancelOverlayLayoutDraft() => ReloadOverlayLayoutDraft();

    private OverlayFrametimeGraphViewModel GetFrametimeGraphItem(string key)
    {
        if (!_frametimeGraphItems.TryGetValue(key, out var item))
            _frametimeGraphItems[key] = item = new OverlayFrametimeGraphViewModel(this);
        return item;
    }

    public bool OverlayLayoutHasChanges()
    {
        if (_config.Overlay.BlocksMigrated) return true;
        var saved = _config.Overlay.Blocks ?? [];
        if (saved.Count != _overlayBlocks.Count) return true;
        for (var index = 0; index < saved.Count; index++)
        {
            var current = _overlayBlocks[index].Config;
            var previous = saved[index];
            if (current.Id != previous.Id || Math.Abs(current.X - previous.X) > 0.0001
                || Math.Abs(current.Y - previous.Y) > 0.0001 || current.Items.Count != previous.Items.Count)
                return true;
            for (var itemIndex = 0; itemIndex < current.Items.Count; itemIndex++)
                if (current.Items[itemIndex].Kind != previous.Items[itemIndex].Kind
                    || current.Items[itemIndex].Value != previous.Items[itemIndex].Value)
                    return true;
        }
        return !OverlayDefinitionsEqual(_config.Overlay.Graphs, _overlayGraphsDraft)
            || !OverlayDefinitionsEqual(_config.Overlay.Templates, _overlayTemplatesDraft);
    }

    private static bool OverlayDefinitionsEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        => System.Text.Json.JsonSerializer.Serialize(left) == System.Text.Json.JsonSerializer.Serialize(right);

    public void SetOverlayCanvasSize(double width, double height)
    {
        width = Math.Clamp(width, 240, 10000);
        height = Math.Clamp(height, 120, 10000);
        if (Math.Abs(OverlayCanvasWidth - width) < 0.5 && Math.Abs(OverlayCanvasHeight - height) < 0.5) return;
        OverlayCanvasWidth = width;
        OverlayCanvasHeight = height;
        foreach (var block in _overlayAppliedBlocks)
            block.SetCanvasSize(width, height);
    }

    public void SetOverlayEditorCanvasSize(double width, double height)
    {
        width = Math.Clamp(width, 240, 10000);
        height = Math.Clamp(height, 120, 10000);
        if (Math.Abs(OverlayEditorCanvasWidth - width) < 0.5 && Math.Abs(OverlayEditorCanvasHeight - height) < 0.5) return;
        OverlayEditorCanvasWidth = width;
        OverlayEditorCanvasHeight = height;
        foreach (var block in _overlayBlocks)
            block.SetCanvasSize(width, height);
    }

    private void ApplyOverlayLayoutDraft()
    {
        BuildOverlayAppliedExtraRows();
        _overlayAppliedBlocks.Clear();
        foreach (var config in _overlayBlocks.Select(block => CloneOverlayBlock(block.Config)))
        {
            var block = new OverlayCanvasBlockViewModel(config, this, static (_, _, _) => { });
            block.SetCanvasSize(OverlayEditorCanvasWidth, OverlayEditorCanvasHeight);
            _overlayAppliedBlocks.Add(block);
        }
        UpdateOverlayRows(_latestSensorData);
    }

    private void ReconcileOverlayBlocks(IReadOnlyList<OverlayMetricsBuilder.OverlayRowPlan> categoryPlans,
        IReadOnlyList<OverlayMetricsBuilder.OverlayRowPlan> instancePlans)
    {
        var categoryRows = OverlayRows.ToDictionary(row => row.Identity, StringComparer.OrdinalIgnoreCase);
        var draftExtras = OverlayExtraRows.ToDictionary(row => row switch
        {
            OverlayGraphRowViewModel graph => ("graph", graph.Identity["#graph:".Length..]),
            OverlayTemplateRowViewModel template => ("template", template.Identity["#template:".Length..]),
            _ => ("", "")
        });
        var appliedExtras = OverlayAppliedExtraRows.ToDictionary(row => row switch
        {
            OverlayGraphRowViewModel graph => ("graph", graph.Identity["#graph:".Length..]),
            OverlayTemplateRowViewModel template => ("template", template.Identity["#template:".Length..]),
            _ => ("", "")
        });

        ReconcileOverlayBlocks(_overlayBlocks, categoryRows, draftExtras, categoryPlans, instancePlans, null);
        var profileSeries = _activeProfile?.ShownSeries is { Count: > 0 } series ? series : null;
        ReconcileOverlayBlocks(_overlayAppliedBlocks, categoryRows, appliedExtras, categoryPlans, instancePlans, profileSeries);
    }

    private void ReconcileOverlayBlocks(IEnumerable<OverlayCanvasBlockViewModel> blocks,
        IReadOnlyDictionary<string, OverlayMetricViewModel> categoryRows,
        IReadOnlyDictionary<(string Kind, string Value), object> extras,
        IReadOnlyList<OverlayMetricsBuilder.OverlayRowPlan> categoryPlans,
        IReadOnlyList<OverlayMetricsBuilder.OverlayRowPlan> instancePlans,
        IReadOnlyList<string>? profileSeries)
    {
        foreach (var block in blocks)
        {
            var desired = new List<object>();
            foreach (var item in block.Config.Items)
            {
                if (item.Kind == "category")
                {
                    foreach (var plan in categoryPlans.Where(plan => OverlayBlockKeys.ForIdentity(plan.Identity) == item.Value))
                        if (categoryRows.TryGetValue(plan.Identity, out var row)) desired.Add(row);
                }
                else if (item.Kind == "series")
                {
                    foreach (var plan in instancePlans.Where(plan => SensorKey.TryParse(plan.Identity, out var key)
                        && (key.IsPrimary
                            ? !item.Value.Contains('*') && key.MatchesPattern(item.Value)
                            : key.MatchesPattern(item.Value)
                                && (profileSeries is null || profileSeries.Count == 0 || profileSeries.Any(key.MatchesPattern)))))
                        if (categoryRows.TryGetValue(plan.Identity, out var row)) desired.Add(row);
                }
                else if (item.Kind == "frametime") desired.Add(GetFrametimeGraphItem(block.Key + ":" + item.Value));
                else if (extras.TryGetValue((item.Kind, item.Value), out var extra)) desired.Add(extra);
            }
            block.ReconcileContent(desired.Distinct(ReferenceEqualityComparer.Instance).ToArray());
        }
    }

    private void CopyOverlayLayoutToConfig()
    {
        _config.Overlay.Blocks = _overlayBlocks.Select(block => CloneOverlayBlock(block.Config)).ToList();
        _config.Overlay.Graphs = _overlayGraphsDraft.Select(CloneOverlayGraph).ToList();
        _config.Overlay.Templates = _overlayTemplatesDraft.Select(CloneOverlayTemplate).ToList();
    }

    private void ReloadOverlayLayoutDraft()
    {
        _overlayGraphsDraft.Clear();
        foreach (var graph in _config.Overlay.Graphs) _overlayGraphsDraft.Add(CloneOverlayGraph(graph));
        _overlayTemplatesDraft.Clear();
        foreach (var template in _config.Overlay.Templates) _overlayTemplatesDraft.Add(CloneOverlayTemplate(template));
        InitializeOverlayBlocks();
        EnsureOverlayExtrasCurrent();
        UpdateOverlayRows(_latestSensorData);
    }

    private static OverlayBlockConfig CloneOverlayBlock(OverlayBlockConfig block) => new()
    {
        Id = block.Id,
        X = block.X,
        Y = block.Y,
        Items = block.Items.Select(item => new OverlayBlockItemConfig { Kind = item.Kind, Value = item.Value }).ToList()
    };

    private static OverlayGraph CloneOverlayGraph(OverlayGraph graph) => new()
    {
        Id = graph.Id, Series = graph.Series, Height = graph.Height, Window = graph.Window,
        Color = graph.Color, Min = graph.Min, Max = graph.Max
    };

    private static OverlayTemplate CloneOverlayTemplate(OverlayTemplate template) => new()
    {
        Id = template.Id, Text = template.Text, Color = template.Color
    };
}
