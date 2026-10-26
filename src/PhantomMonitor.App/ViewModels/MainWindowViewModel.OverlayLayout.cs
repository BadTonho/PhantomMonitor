using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<string, OverlayFrametimeGraphViewModel> _frametimeGraphItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _overlayHiddenMetricsDraft = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _overlayHiddenMetricsApplied = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasOverlayReferenceArea;

    public IReadOnlyList<string> OverlayHiddenMetricsDraft
        => _overlayHiddenMetricsDraft.OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool IsOverlayMetricHiddenInDraft(string identity)
        => _overlayHiddenMetricsDraft.Contains(identity);

    public IReadOnlyList<OverlayHiddenMetricOption> GetOverlayHiddenMetricOptions()
        => _overlayHiddenMetricsDraft
            .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase)
            .Select(identity =>
            {
                var row = OverlayRows.FirstOrDefault(candidate =>
                    candidate.OverrideIdentity.Equals(identity, StringComparison.OrdinalIgnoreCase));
                return new OverlayHiddenMetricOption(identity, row is null ? identity : $"{row.Name} · {row.Value}");
            })
            .ToArray();

    public bool HideOverlayMetricDraft(string identity)
    {
        identity = (identity ?? "").Trim();
        if (identity.Length == 0 || !_overlayHiddenMetricsDraft.Add(identity)) return false;
        UpdateOverlayRows(_latestSensorData);
        return true;
    }

    public bool RestoreOverlayMetricsDraft(IEnumerable<string> identities)
    {
        var changed = false;
        foreach (var identity in identities)
            changed |= _overlayHiddenMetricsDraft.Remove(identity);
        if (changed) UpdateOverlayRows(_latestSensorData);
        return changed;
    }

    private void ReloadOverlayHiddenMetricDraft()
    {
        _overlayHiddenMetricsDraft.Clear();
        _overlayHiddenMetricsDraft.UnionWith(_config.Overlay.HiddenMetrics ?? []);
        _overlayHiddenMetricsApplied = new HashSet<string>(_config.Overlay.HiddenMetrics ?? [], StringComparer.OrdinalIgnoreCase);
    }

    private void InitializeOverlayBlocks()
    {
        ReloadOverlayHiddenMetricDraft();
        foreach (var block in _overlayBlocks.Concat(_overlayAppliedBlocks))
            block.DisposeMetrics();
        _overlayBlocks.Clear();
        _overlayAppliedBlocks.Clear();
        foreach (var config in _config.Overlay.Blocks ?? [])
        {
            var blockConfig = CloneOverlayBlock(config);
            var block = new OverlayCanvasBlockViewModel(blockConfig, this, SetOverlayBlockPosition);
            block.MarkAsPreview();
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
        var config = new OverlayBlockConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            X = double.IsFinite(x) ? x : 0,
            Y = double.IsFinite(y) ? y : 0
        };
        var block = new OverlayCanvasBlockViewModel(config, this, SetOverlayBlockPosition);
        block.MarkAsPreview();
        block.SetCanvasSize(OverlayEditorCanvasWidth, OverlayEditorCanvasHeight);
        _overlayBlocks.Add(block);
        return block;
    }

    public void RemoveOverlayBlock(OverlayCanvasBlockViewModel block)
    {
        if (!_overlayBlocks.Remove(block)) return;
        block.DisposeMetrics();
        UpdateOverlayRows(_latestSensorData);
    }

    public void SetOverlayBlockDefinition(OverlayCanvasBlockViewModel block, IEnumerable<OverlayBlockItemConfig> items,
        IEnumerable<OverlayGraph> graphs, IEnumerable<OverlayTemplate> templates,
        IEnumerable<OverlayMetricOverrideConfig> metricOverrides,
        IReadOnlyDictionary<string, List<string>> metricOrder, IEnumerable<OverlayCustomLineConfig> customLines)
    {
        block.Config.Items = items.Select(item => new OverlayBlockItemConfig { Kind = item.Kind, Value = item.Value }).ToList();
        block.Config.MetricOverrides = metricOverrides.Select(CloneOverlayMetricOverride).ToList();
        block.Config.CustomLines = customLines.Select(CloneOverlayCustomLine).ToList();
        block.Config.MetricOrder = metricOrder.Count == 0 ? null
            : metricOrder.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.OrdinalIgnoreCase);
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

    private OverlayFrametimeGraphViewModel GetFrametimeGraphItem(OverlayCanvasBlockViewModel block, string key)
    {
        if (!_frametimeGraphItems.TryGetValue(key, out var item))
            _frametimeGraphItems[key] = item = new OverlayFrametimeGraphViewModel(this, block);
        return item;
    }

    public bool OverlayLayoutHasChanges()
    {
        if (_config.Overlay.BlocksMigrated) return true;
        if (!_overlayHiddenMetricsDraft.SetEquals(_config.Overlay.HiddenMetrics ?? [])) return true;
        var saved = _config.Overlay.Blocks ?? [];
        if (saved.Count != _overlayBlocks.Count) return true;
        for (var index = 0; index < saved.Count; index++)
        {
            var current = _overlayBlocks[index].Config;
            var previous = saved[index];
            if (current.Id != previous.Id || Math.Abs(current.X - previous.X) > 0.0001
                || Math.Abs(current.Y - previous.Y) > 0.0001 || current.Items.Count != previous.Items.Count
                || !OverlayDefinitionsEqual(current.MetricOverrides, previous.MetricOverrides)
                || !OverlayDefinitionsEqual(current.CustomLines, previous.CustomLines)
                || !MetricOrdersEqual(current.MetricOrder, previous.MetricOrder))
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

    private static bool MetricOrdersEqual(Dictionary<string, List<string>>? left, Dictionary<string, List<string>>? right)
    {
        left ??= new Dictionary<string, List<string>>();
        right ??= new Dictionary<string, List<string>>();
        if (left.Count != right.Count) return false;
        return left.All(entry => right.TryGetValue(entry.Key, out var identities)
            && entry.Value.SequenceEqual(identities, StringComparer.OrdinalIgnoreCase));
    }

    public void SetOverlayCanvasSize(double width, double height)
    {
        width = Math.Clamp(width, 240, 10000);
        height = Math.Clamp(height, 120, 10000);
        _hasOverlayReferenceArea = true;
        if (Math.Abs(OverlayCanvasWidth - width) < 0.5 && Math.Abs(OverlayCanvasHeight - height) < 0.5) return;
        OverlayCanvasWidth = width;
        OverlayCanvasHeight = height;
        foreach (var block in _overlayAppliedBlocks)
            block.SetCanvasSize(width, height);
        foreach (var block in _overlayBlocks)
            block.RefreshCanvasPosition();
        PublishInGameOverlayFrame();
    }

    public void SetOverlayCanvasSizeIfMissing(double width, double height)
    {
        if (_hasOverlayReferenceArea) return;
        SetOverlayCanvasSize(width, height);
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
        _config.Overlay.HiddenMetrics = OverlayHiddenMetricsDraft.ToList();
        _overlayHiddenMetricsApplied = new HashSet<string>(_config.Overlay.HiddenMetrics, StringComparer.OrdinalIgnoreCase);
        BuildOverlayAppliedExtraRows();
        foreach (var block in _overlayAppliedBlocks)
            block.DisposeMetrics();
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

        var profileSeries = _activeProfile?.ShownSeries is { Count: > 0 } series ? series : null;
        ReconcileOverlayBlocks(_overlayBlocks, categoryRows, draftExtras, categoryPlans, instancePlans, profileSeries,
            _overlayHiddenMetricsDraft);
        ReconcileOverlayBlocks(_overlayAppliedBlocks, categoryRows, appliedExtras, categoryPlans, instancePlans, profileSeries,
            _overlayHiddenMetricsApplied);
    }

    private void ReconcileOverlayBlocks(IEnumerable<OverlayCanvasBlockViewModel> blocks,
        IReadOnlyDictionary<string, OverlayMetricViewModel> categoryRows,
        IReadOnlyDictionary<(string Kind, string Value), object> extras,
        IReadOnlyList<OverlayMetricsBuilder.OverlayRowPlan> categoryPlans,
        IReadOnlyList<OverlayMetricsBuilder.OverlayRowPlan> instancePlans,
        IReadOnlyList<string>? profileSeries,
        IReadOnlySet<string> hiddenMetrics)
    {
        foreach (var block in blocks)
        {
            var desired = new List<object>();
            var seenMetricIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in block.Config.Items)
            {
                if (item.Kind == "category")
                {
                    var rows = categoryPlans.Where(plan => OverlayBlockKeys.ForIdentity(plan.Identity) == item.Value)
                        .Select(plan => categoryRows.TryGetValue(plan.Identity, out var row) ? row : null)
                        .Where(row => row is not null).Cast<OverlayMetricViewModel>();
                    AppendOrderedMetricRows(block, item, rows, desired, seenMetricIdentities, hiddenMetrics);
                }
                else if (item.Kind == "series")
                {
                    var rows = instancePlans.Where(plan => OverlayBlockContentRules.IsSeriesSelected(
                            plan.Identity, item.Value, profileSeries))
                        .Select(plan => categoryRows.TryGetValue(plan.Identity, out var row) ? row : null)
                        .Where(row => row is not null).Cast<OverlayMetricViewModel>();
                    AppendOrderedMetricRows(block, item, rows, desired, seenMetricIdentities, hiddenMetrics);
                }
                else if (item.Kind == "frametime")
                    desired.Add(GetFrametimeGraphItem(block,
                        block.Key + (block.IsPreview ? ":draft:" : ":applied:") + item.Value));
                else if (item.Kind == "customline")
                {
                    var line = block.Config.CustomLines.FirstOrDefault(candidate => candidate.Id == item.Value);
                    if (line is not null) desired.Add(block.GetCustomLine(line, OverlayHardwareDevices));
                }
                else if (extras.TryGetValue((item.Kind, item.Value), out var extra)) desired.Add(extra);
            }
            block.ReconcileContent(desired.Distinct(ReferenceEqualityComparer.Instance).ToArray());
        }
    }

    private static void AppendOrderedMetricRows(OverlayCanvasBlockViewModel block, OverlayBlockItemConfig item,
        IEnumerable<OverlayMetricViewModel> rows, ICollection<object> desired, ISet<string> seenIdentities,
        IReadOnlySet<string> hiddenMetrics)
    {
        var groupKey = $"{item.Kind.ToLowerInvariant()}:{item.Value}";
        var order = block.Config.MetricOrder is { } metricOrder && metricOrder.TryGetValue(groupKey, out var savedOrder)
            ? savedOrder : [];
        var positions = order.Select((identity, index) => (identity, index))
            .ToDictionary(pair => pair.identity, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        var ordered = rows.Select((row, index) => (row, index))
            .OrderBy(pair => positions.TryGetValue(pair.row.OverrideIdentity, out var position) ? position : int.MaxValue)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.row);
        foreach (var row in ordered)
            if (!hiddenMetrics.Contains(row.OverrideIdentity) && seenIdentities.Add(row.OverrideIdentity))
                desired.Add(block.GetMetricViewModel(row));
    }

    private void CopyOverlayLayoutToConfig()
    {
        _config.Overlay.Blocks = _overlayBlocks.Select(block => CloneOverlayBlock(block.Config)).ToList();
        _config.Overlay.HiddenMetrics = OverlayHiddenMetricsDraft.ToList();
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

    internal static OverlayBlockConfig CloneOverlayBlock(OverlayBlockConfig block) => new()
    {
        Id = block.Id,
        X = block.X,
        Y = block.Y,
        Items = block.Items.Select(item => new OverlayBlockItemConfig { Kind = item.Kind, Value = item.Value }).ToList(),
        MetricOverrides = (block.MetricOverrides ?? []).Select(CloneOverlayMetricOverride).ToList(),
        CustomLines = (block.CustomLines ?? []).Select(CloneOverlayCustomLine).ToList(),
        MetricOrder = block.MetricOrder is { Count: > 0 } metricOrder
            ? metricOrder.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.OrdinalIgnoreCase)
            : null
    };

    internal static OverlayCustomLineConfig CloneOverlayCustomLine(OverlayCustomLineConfig line) => new()
    {
        Id = line.Id, Text = line.Text, Width = line.Width, FontSize = line.FontSize,
        Hardware = (line.Hardware ?? []).Select(reference => new OverlayHardwareReference
        {
            Token = reference.Token, DeviceId = reference.DeviceId, Category = reference.Category
        }).ToList()
    };

    private static OverlayMetricOverrideConfig CloneOverlayMetricOverride(OverlayMetricOverrideConfig metricOverride) => new()
    {
        Identity = metricOverride.Identity,
        DisplayName = metricOverride.DisplayName,
        Decimals = metricOverride.Decimals,
        Color = metricOverride.Color
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
