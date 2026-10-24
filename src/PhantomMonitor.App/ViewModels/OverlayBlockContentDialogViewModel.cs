using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

/// <summary>Draft state and editing operations for one overlay block's content dialog.</summary>
public sealed partial class OverlayBlockContentDialogViewModel : INotifyPropertyChanged
{
    private const string AllSeriesCategories = "Todas";

    private static readonly (string Key, string Name)[] Categories =
    [
        ("cpu", "CPU"), ("gpu", "GPU"), ("ram", "RAM"), ("disk", "Disco"),
        ("network", "Rede"), ("fans", "Ventoinhas"), ("motherboard", "Placa-mãe"), ("fps", "FPS / frametime")
    ];

    private readonly OverlayCanvasBlockViewModel _block;
    private readonly MainWindowViewModel _owner;
    private readonly IReadOnlyList<string> _detectedSeries;
    private readonly HashSet<(string Kind, string Value)> _selected;
    private readonly List<OverlayGraph> _graphs;
    private readonly List<OverlayTemplate> _templates;
    private string? _editingGraphId;
    private string? _editingTemplateId;
    private string _seriesPatternInput = "";
    private string _selectedSeriesCategory = AllSeriesCategories;
    private string _graphSeries = "";
    private string _graphHeight = "36";
    private string _graphWindow = "240";
    private string _graphMin = "";
    private string _graphMax = "";
    private string _graphColor = "";
    private string _templateText = "";
    private string _templateColor = "";
    private OverlayBlockItemChoiceViewModel? _selectedContentItem;
    private OverlayBlockOrderRowViewModel? _selectedOrderRow;
    private readonly Dictionary<string, List<string>> _metricOrder;
    private readonly Dictionary<string, OverlayMetricEditChoiceViewModel> _metricEditChoices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _customMetricOrderGroups = new(StringComparer.OrdinalIgnoreCase);
    private bool _isPickerDraft;

    public OverlayBlockContentDialogViewModel(MainWindowViewModel viewModel, OverlayCanvasBlockViewModel block)
    {
        _owner = viewModel;
        _owner.OverlayRows.CollectionChanged += OnOverlayRowsChanged;
        _block = block;
        _detectedSeries = viewModel.OverlaySeriesOptions.ToArray();
        _graphs = viewModel.OverlayGraphsDraft.Select(Clone).ToList();
        _templates = viewModel.OverlayTemplatesDraft.Select(Clone).ToList();
        _metricOrder = (block.Config.MetricOrder ?? []).ToDictionary(entry => entry.Key,
            entry => entry.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        _customMetricOrderGroups.UnionWith(_metricOrder.Keys);
        _selected = block.Config.Items.Select(item => (item.Kind, item.Value)).ToHashSet();
        var addedItems = new HashSet<(string Kind, string Value)>();
        foreach (var item in block.Config.Items)
            if (addedItems.Add((item.Kind, item.Value)))
                OrderedContentItems.Add(new OverlayBlockItemChoiceViewModel(item.Kind, item.Value, GetItemLabel(item.Kind, item.Value)));
        foreach (var metricOverride in block.Config.MetricOverrides ?? [])
            _metricOverrides[metricOverride.Identity] = Clone(metricOverride);

        foreach (var (key, name) in Categories)
            CategoriesChoices.Add(CreateChoice("category", key, name));
        FrametimeChoice = CreateChoice("frametime", "frametime", "Gráfico de frametime");
        RefreshDynamicChoices();
        RebuildOrderRows();
    }

    private OverlayBlockContentDialogViewModel(OverlayBlockContentDialogViewModel source)
    {
        _isPickerDraft = true;
        _owner = source._owner;
        _block = source._block;
        _detectedSeries = source._detectedSeries;
        _selected = new HashSet<(string Kind, string Value)>(source._selected);
        _graphs = source._graphs.Select(Clone).ToList();
        _templates = source._templates.Select(Clone).ToList();
        _metricOrder = source._metricOrder.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        _customMetricOrderGroups.UnionWith(source._customMetricOrderGroups);
        _selectedSeriesCategory = source._selectedSeriesCategory;
        _seriesPatternInput = source._seriesPatternInput;
        _graphSeries = source._graphSeries;
        _graphHeight = source._graphHeight;
        _graphWindow = source._graphWindow;
        _graphMin = source._graphMin;
        _graphMax = source._graphMax;
        _graphColor = source._graphColor;
        _templateText = source._templateText;
        _templateColor = source._templateColor;
        _editingGraphId = source._editingGraphId;
        _editingTemplateId = source._editingTemplateId;

        foreach (var item in source.OrderedContentItems)
            OrderedContentItems.Add(new OverlayBlockItemChoiceViewModel(item.Kind, item.Value, item.Label));
        foreach (var (key, name) in Categories)
            CategoriesChoices.Add(CreateChoice("category", key, name));
        FrametimeChoice = CreateChoice("frametime", "frametime", "Gráfico de frametime");
        if (source.SelectedContentItem is { } selected)
            SelectedContentItem = OrderedContentItems.FirstOrDefault(item => item.Kind == selected.Kind
                && item.Value.Equals(selected.Value, StringComparison.OrdinalIgnoreCase));
        RefreshDynamicChoices();
        RebuildOrderRows();
    }

    public ObservableCollection<OverlayBlockContentChoiceViewModel> CategoriesChoices { get; } = [];
    public OverlayBlockContentChoiceViewModel FrametimeChoice { get; private set; }
    public IReadOnlyList<string> SeriesCategories { get; } =
    [
        AllSeriesCategories, "CPU", "GPU", "RAM", "Disco", "Rede", "Ventoinhas",
        "Placa-mãe", "Bateria", "FPS", "Outros"
    ];
    public ObservableCollection<OverlayBlockContentChoiceViewModel> SeriesChoices { get; } = [];
    public ObservableCollection<OverlayBlockContentChoiceViewModel> GraphChoices { get; } = [];
    public ObservableCollection<OverlayBlockContentChoiceViewModel> TemplateChoices { get; } = [];
    public ObservableCollection<OverlayBlockItemChoiceViewModel> OrderedContentItems { get; } = [];
    public ObservableCollection<OverlayBlockOrderRowViewModel> OrderRows { get; } = [];
    public OverlayBlockItemChoiceViewModel? SelectedContentItem
    {
        get => _selectedContentItem;
        set
        {
            if (!Set(ref _selectedContentItem, value)) return;
            Raise(nameof(CanMoveContentUp));
            Raise(nameof(CanMoveContentDown));
            Raise(nameof(ShowMetricEditorHint));
            RefreshMetricChoices();
        }
    }

    public OverlayBlockOrderRowViewModel? SelectedOrderRow
    {
        get => _selectedOrderRow;
        set
        {
            if (!Set(ref _selectedOrderRow, value)) return;
            SelectedContentItem = value?.Group;
            if (value?.Metric is { } metric)
            {
                if (!MetricChoices.Any(choice => choice.Identity.Equals(metric.Identity, StringComparison.OrdinalIgnoreCase)))
                    RefreshMetricChoicesFor(value.Group);
                SelectedMetric = MetricChoices.FirstOrDefault(choice => choice.Identity.Equals(metric.Identity, StringComparison.OrdinalIgnoreCase));
            }
            else
                SelectedMetric = null;
            Raise(nameof(CanMoveContentUp));
            Raise(nameof(CanMoveContentDown));
            Raise(nameof(CanRemoveSelectedContent));
            Raise(nameof(RemoveSelectedContentText));
            Raise(nameof(HasSelectedMetric));
        }
    }

    public bool HasSelectedMetric => SelectedOrderRow?.Metric is not null;
    public bool ShowMetricEditorHint => !HasSelectedMetric;
    public bool CanRemoveSelectedContent => SelectedOrderRow is not null;
    public string RemoveSelectedContentText => SelectedOrderRow?.IsMetric == true
        ? "Ocultar em todos os blocos"
        : "Remover da lista";
    public bool CanMoveContentUp => CanMoveSelectedOrderRow(-1);
    public bool CanMoveContentDown => CanMoveSelectedOrderRow(1);

    public IReadOnlyList<OverlayHiddenMetricOption> GetHiddenMetricOptions()
        => _owner.GetOverlayHiddenMetricOptions();

    public void RestoreHiddenMetrics(IEnumerable<string> identities)
    {
        if (!_owner.RestoreOverlayMetricsDraft(identities)) return;
        RefreshDynamicChoices();
        RebuildOrderRows();
    }

    public string SelectedSeriesCategory
    {
        get => _selectedSeriesCategory;
        set
        {
            if (!Set(ref _selectedSeriesCategory, value)) return;
            RefreshSeriesChoices();
        }
    }

    /// <summary>True when all visible series are selected, false when none, null when selection is partial.</summary>
    public bool? SeriesCategorySelectionState
    {
        get
        {
            if (SeriesChoices.Count == 0) return false;
            var selectedCount = SeriesChoices.Count(choice => choice.IsSelected);
            return selectedCount == 0 ? false : selectedCount == SeriesChoices.Count ? true : null;
        }
    }

    public void ToggleVisibleSeriesSelection()
    {
        var selectAll = SeriesCategorySelectionState != true;
        foreach (var choice in SeriesChoices)
            choice.IsSelected = selectAll;
        Raise(nameof(SeriesCategorySelectionState));
    }

    public bool HasSeriesChoices => SeriesChoices.Count > 0;
    public bool NoSeriesChoices => !HasSeriesChoices;

    public string SeriesPatternInput { get => _seriesPatternInput; set => Set(ref _seriesPatternInput, value); }
    public string GraphSeries { get => _graphSeries; set => Set(ref _graphSeries, value); }
    public string GraphHeight { get => _graphHeight; set => Set(ref _graphHeight, value); }
    public string GraphWindow { get => _graphWindow; set => Set(ref _graphWindow, value); }
    public string GraphMin { get => _graphMin; set => Set(ref _graphMin, value); }
    public string GraphMax { get => _graphMax; set => Set(ref _graphMax, value); }
    public string GraphColor { get => _graphColor; set => Set(ref _graphColor, value); }
    public string TemplateText { get => _templateText; set => Set(ref _templateText, value); }
    public string TemplateColor { get => _templateColor; set => Set(ref _templateColor, value); }

    public OverlayBlockContentDialogViewModel CreateContentPickerDraft() => new(this);

    public void Dispose()
    {
        _owner.OverlayRows.CollectionChanged -= OnOverlayRowsChanged;
        foreach (var choice in _metricEditChoices.Values)
            choice.Dispose();
        _metricEditChoices.Clear();
    }

    private void OnOverlayRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDynamicChoices();
    }

    public void CommitContentPickerDraft(OverlayBlockContentDialogViewModel pickerDraft)
    {
        var selectedItem = pickerDraft.SelectedContentItem;
        SelectedContentItem = null;
        _selected.Clear();
        _selected.UnionWith(pickerDraft._selected);

        _graphs.Clear();
        _graphs.AddRange(pickerDraft._graphs.Select(Clone));
        _templates.Clear();
        _templates.AddRange(pickerDraft._templates.Select(Clone));

        OrderedContentItems.Clear();
        foreach (var item in pickerDraft.OrderedContentItems)
            OrderedContentItems.Add(new OverlayBlockItemChoiceViewModel(item.Kind, item.Value, GetItemLabel(item.Kind, item.Value)));
        _metricOrder.Clear();
        foreach (var entry in pickerDraft._metricOrder) _metricOrder[entry.Key] = entry.Value.ToList();
        _customMetricOrderGroups.Clear();
        _customMetricOrderGroups.UnionWith(pickerDraft._customMetricOrderGroups);

        CategoriesChoices.Clear();
        foreach (var (key, name) in Categories)
            CategoriesChoices.Add(CreateChoice("category", key, name));
        FrametimeChoice = CreateChoice("frametime", "frametime", "Gráfico de frametime");
        Raise(nameof(FrametimeChoice));

        SelectedSeriesCategory = pickerDraft.SelectedSeriesCategory;
        SeriesPatternInput = pickerDraft.SeriesPatternInput;
        GraphSeries = pickerDraft.GraphSeries;
        GraphHeight = pickerDraft.GraphHeight;
        GraphWindow = pickerDraft.GraphWindow;
        GraphMin = pickerDraft.GraphMin;
        GraphMax = pickerDraft.GraphMax;
        GraphColor = pickerDraft.GraphColor;
        TemplateText = pickerDraft.TemplateText;
        TemplateColor = pickerDraft.TemplateColor;
        _editingGraphId = pickerDraft._editingGraphId;
        _editingTemplateId = pickerDraft._editingTemplateId;

        SeriesChoices.Clear();
        GraphChoices.Clear();
        TemplateChoices.Clear();
        RefreshDynamicChoices();
        SelectedContentItem = selectedItem is null
            ? null
            : OrderedContentItems.FirstOrDefault(item => item.Kind == selectedItem.Kind
                && item.Value.Equals(selectedItem.Value, StringComparison.OrdinalIgnoreCase));
        RebuildOrderRows();
        PublishDraftToCanvas();
    }

    public void AddSeriesPattern()
    {
        var value = SeriesPatternInput.Trim();
        if (!IsValidPattern(value) || _selected.Contains(("series", value))) return;
        SetSelected("series", value, true);
        SeriesPatternInput = "";
        RefreshDynamicChoices();
    }

    public void MoveSelectedContent(int offset)
    {
        if (SelectedOrderRow is null) return;
        if (!SelectedOrderRow.IsMetric)
        {
            var currentGroup = OrderedContentItems.IndexOf(SelectedOrderRow.Group);
            MoveContent(currentGroup, currentGroup + offset);
            SelectedOrderRow = OrderRows.FirstOrDefault(row => !row.IsMetric && ReferenceEquals(row.Group, SelectedContentItem));
            return;
        }
        var index = OrderRows.IndexOf(SelectedOrderRow);
        MoveOrderRow(index, index + offset);
    }

    public void MoveContent(int sourceIndex, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= OrderedContentItems.Count
            || targetIndex < 0 || targetIndex >= OrderedContentItems.Count
            || sourceIndex == targetIndex) return;
        var item = OrderedContentItems[sourceIndex];
        OrderedContentItems.Move(sourceIndex, targetIndex);
        SelectedContentItem = item;
        Raise(nameof(CanMoveContentUp));
        Raise(nameof(CanMoveContentDown));
        RebuildOrderRows();
        PublishDraftToCanvas();
    }

    public void MoveOrderRow(int sourceIndex, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= OrderRows.Count || targetIndex < 0 || targetIndex >= OrderRows.Count
            || sourceIndex == targetIndex) return;
        var row = OrderRows[sourceIndex];
        if (!row.IsMetric)
        {
            var sourceGroupIndex = OrderedContentItems.IndexOf(row.Group);
            var targetGroupIndex = OrderedContentItems.IndexOf(OrderRows[targetIndex].Group);
            if (sourceGroupIndex < 0 || targetGroupIndex < 0) return;
            MoveContent(sourceGroupIndex, targetGroupIndex);
            SelectedOrderRow = OrderRows.FirstOrDefault(candidate => !candidate.IsMetric && ReferenceEquals(candidate.Group, row.Group));
            return;
        }

        var targetRow = OrderRows[targetIndex];
        if (!targetRow.IsMetric || !targetRow.Group.Kind.Equals(row.Group.Kind, StringComparison.OrdinalIgnoreCase)
            || !targetRow.Group.Value.Equals(row.Group.Value, StringComparison.OrdinalIgnoreCase)) return;
        var groupKey = row.GroupKey;
        var siblings = OrderRows.Where(candidate => candidate.IsMetric && candidate.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase)).ToList();
        var from = siblings.IndexOf(row);
        var to = siblings.IndexOf(targetRow);
        if (from < 0 || to < 0) return;
        siblings.RemoveAt(from);
        siblings.Insert(to, row);
        _metricOrder[groupKey] = siblings.Select(candidate => candidate.Identity).ToList();
        _customMetricOrderGroups.Add(groupKey);
        RebuildOrderRows();
        SelectedOrderRow = OrderRows.FirstOrDefault(candidate => candidate.IsMetric
            && candidate.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase)
            && candidate.Identity.Equals(row.Identity, StringComparison.OrdinalIgnoreCase));
        PublishDraftToCanvas();
    }

    public void MoveOrderRowToInsertion(int sourceIndex, int insertionIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= OrderRows.Count) return;
        insertionIndex = Math.Clamp(insertionIndex, 0, OrderRows.Count);
        var row = OrderRows[sourceIndex];
        if (!row.IsMetric)
        {
            var sourceGroupIndex = OrderedContentItems.IndexOf(row.Group);
            if (sourceGroupIndex < 0) return;
            var targetGroupIndex = OrderRows.Take(insertionIndex).Count(candidate => !candidate.IsMetric);
            if (sourceGroupIndex < targetGroupIndex) targetGroupIndex--;
            targetGroupIndex = Math.Clamp(targetGroupIndex, 0, OrderedContentItems.Count - 1);
            MoveContent(sourceGroupIndex, targetGroupIndex);
            SelectedOrderRow = OrderRows.FirstOrDefault(candidate => !candidate.IsMetric && ReferenceEquals(candidate.Group, row.Group));
            return;
        }

        var groupKey = row.GroupKey;
        var siblings = OrderRows.Where(candidate => candidate.IsMetric
            && candidate.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase)).ToList();
        var from = siblings.IndexOf(row);
        if (from < 0) return;
        var to = OrderRows.Take(insertionIndex).Count(candidate => candidate.IsMetric
            && candidate.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase));
        if (from < to) to--;
        to = Math.Clamp(to, 0, siblings.Count - 1);
        if (from == to) return;
        siblings.RemoveAt(from);
        siblings.Insert(to, row);
        _metricOrder[groupKey] = siblings.Select(candidate => candidate.Identity).ToList();
        _customMetricOrderGroups.Add(groupKey);
        RebuildOrderRows();
        SelectedOrderRow = OrderRows.FirstOrDefault(candidate => candidate.IsMetric
            && candidate.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase)
            && candidate.Identity.Equals(row.Identity, StringComparison.OrdinalIgnoreCase));
        PublishDraftToCanvas();
    }

    public OverlayBlockOrderRowViewModel? FindCurrentOrderRow(OverlayBlockOrderRowViewModel row)
        => OrderRows.FirstOrDefault(candidate => candidate.IsMetric == row.IsMetric
            && candidate.GroupKey.Equals(row.GroupKey, StringComparison.OrdinalIgnoreCase)
            && candidate.Identity.Equals(row.Identity, StringComparison.OrdinalIgnoreCase));

    private bool CanMoveSelectedOrderRow(int offset)
    {
        if (SelectedOrderRow is null) return false;
        if (!SelectedOrderRow.IsMetric)
        {
            var groupIndex = OrderedContentItems.IndexOf(SelectedOrderRow.Group);
            return groupIndex + offset >= 0 && groupIndex + offset < OrderedContentItems.Count;
        }
        var current = OrderRows.IndexOf(SelectedOrderRow);
        var target = current + offset;
        if (target < 0 || target >= OrderRows.Count) return false;
        var targetRow = OrderRows[target];
        return !SelectedOrderRow.IsMetric || targetRow.IsMetric
            && targetRow.GroupKey.Equals(SelectedOrderRow.GroupKey, StringComparison.OrdinalIgnoreCase);
    }

    public void RemoveSelectedContent()
    {
        if (SelectedOrderRow?.Metric is { } metric)
        {
            if (_owner.HideOverlayMetricDraft(metric.Identity))
            {
                RefreshDynamicChoices();
                RebuildOrderRows();
            }
            return;
        }

        if (SelectedContentItem is not { } item) return;
        var choice = FindChoice(item.Kind, item.Value);
        if (choice is not null)
            choice.IsSelected = false;
        else
            SetSelected(item.Kind, item.Value, false);
    }

    public void RefreshDynamicChoices()
    {
        CaptureSelection(SeriesChoices);
        CaptureSelection(GraphChoices);
        CaptureSelection(TemplateChoices);

        RefreshSeriesChoices();

        GraphChoices.Clear();
        foreach (var graph in _graphs)
            GraphChoices.Add(CreateChoice("graph", graph.Id, graph.Series, () => _editingGraphId = graph.Id));

        TemplateChoices.Clear();
        foreach (var template in _templates)
            TemplateChoices.Add(CreateChoice("template", template.Id, template.Text, () => _editingTemplateId = template.Id));
        foreach (var item in OrderedContentItems)
            item.UpdateLabel(GetItemLabel(item.Kind, item.Value));
        RebuildOrderRows();
    }

    private void RefreshSeriesChoices()
    {
        SeriesChoices.Clear();
        var series = _detectedSeries
            .Concat(_block.Config.Items.Where(item => item.Kind == "series").Select(item => item.Value))
            .Concat(_selected.Where(item => item.Kind == "series").Select(item => item.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(value => _selectedSeriesCategory == AllSeriesCategories
                || string.Equals(SeriesCategory(value), _selectedSeriesCategory, StringComparison.OrdinalIgnoreCase))
            .OrderBy(value => value, SeriesValueComparer.Instance);
        foreach (var value in series)
            SeriesChoices.Add(CreateChoice("series", value, value, selectionChanged: _ => Raise(nameof(SeriesCategorySelectionState))));
        Raise(nameof(HasSeriesChoices));
        Raise(nameof(NoSeriesChoices));
        Raise(nameof(SeriesCategorySelectionState));
    }

    public void EditGraph(string id)
    {
        var graph = _graphs.FirstOrDefault(item => item.Id == id);
        if (graph is null) return;
        _editingGraphId = graph.Id;
        GraphSeries = graph.Series;
        GraphHeight = graph.Height.ToString(CultureInfo.InvariantCulture);
        GraphWindow = graph.Window.ToString(CultureInfo.InvariantCulture);
        GraphMin = graph.Min?.ToString(CultureInfo.InvariantCulture) ?? "";
        GraphMax = graph.Max?.ToString(CultureInfo.InvariantCulture) ?? "";
        GraphColor = graph.Color ?? "";
    }

    public void NewGraph()
    {
        _editingGraphId = null;
        GraphSeries = "";
        GraphHeight = "36";
        GraphWindow = "240";
        GraphMin = "";
        GraphMax = "";
        GraphColor = "";
    }

    public void SaveGraph()
    {
        var series = GraphSeries.Trim();
        if (series.Length == 0 || !double.TryParse(GraphHeight, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            || !int.TryParse(GraphWindow, NumberStyles.Integer, CultureInfo.InvariantCulture, out var window)) return;
        double? min = double.TryParse(GraphMin, NumberStyles.Float, CultureInfo.InvariantCulture, out var minValue) ? minValue : null;
        double? max = double.TryParse(GraphMax, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxValue) ? maxValue : null;
        var graph = _graphs.FirstOrDefault(item => item.Id == _editingGraphId);
        if (graph is null)
        {
            graph = new OverlayGraph { Id = Guid.NewGuid().ToString("N") };
            _graphs.Add(graph);
        }
        graph.Series = series;
        graph.Height = Math.Clamp(height, 8, 120);
        graph.Window = Math.Clamp(window, 30, 1200);
        graph.Min = min;
        graph.Max = max;
        graph.Color = EmptyToNull(GraphColor);
        SetSelected("graph", graph.Id, true, publishDraft: false);
        _editingGraphId = graph.Id;
        RefreshDynamicChoices();
        PublishDraftToCanvas();
    }

    public void RemoveGraph()
    {
        if (_editingGraphId is null) return;
        _graphs.RemoveAll(item => item.Id == _editingGraphId);
        SetSelected("graph", _editingGraphId, false, publishDraft: false);
        NewGraph();
        RefreshDynamicChoices();
        PublishDraftToCanvas();
    }

    public void EditTemplate(string id)
    {
        var template = _templates.FirstOrDefault(item => item.Id == id);
        if (template is null) return;
        _editingTemplateId = template.Id;
        TemplateText = template.Text;
        TemplateColor = template.Color ?? "";
    }

    public void NewTemplate()
    {
        _editingTemplateId = null;
        TemplateText = "";
        TemplateColor = "";
    }

    public void SaveTemplate()
    {
        var text = TemplateText.Trim();
        if (text.Length == 0) return;
        var template = _templates.FirstOrDefault(item => item.Id == _editingTemplateId);
        if (template is null)
        {
            template = new OverlayTemplate { Id = Guid.NewGuid().ToString("N") };
            _templates.Add(template);
        }
        template.Text = text;
        template.Color = EmptyToNull(TemplateColor);
        SetSelected("template", template.Id, true, publishDraft: false);
        _editingTemplateId = template.Id;
        RefreshDynamicChoices();
        PublishDraftToCanvas();
    }

    public void RemoveTemplate()
    {
        if (_editingTemplateId is null) return;
        _templates.RemoveAll(item => item.Id == _editingTemplateId);
        SetSelected("template", _editingTemplateId, false, publishDraft: false);
        NewTemplate();
        RefreshDynamicChoices();
        PublishDraftToCanvas();
    }

    public void Apply()
    {
        CaptureSelection(CategoriesChoices);
        CaptureSelection([FrametimeChoice]);
        CaptureSelection(SeriesChoices);
        CaptureSelection(GraphChoices);
        CaptureSelection(TemplateChoices);
        PublishDraftToCanvas();
    }

    private void PublishDraftToCanvas()
    {
        if (_isPickerDraft) return;

        var items = OrderedContentItems
            .Select(item => new OverlayBlockItemConfig { Kind = item.Kind, Value = item.Value })
            .ToArray();
        var metricOrder = _customMetricOrderGroups.ToDictionary(key => key,
            key => _metricOrder.TryGetValue(key, out var identities) ? identities.ToList() : [], StringComparer.OrdinalIgnoreCase);
        _owner.SetOverlayBlockDefinition(_block, items, _graphs, _templates,
            _metricOverrides.Values, metricOrder);
    }

    public static string GetMetricOrderKey(string kind, string value) => $"{kind.ToLowerInvariant()}:{value}";

    private void RebuildOrderRows()
    {
        var selected = _selectedOrderRow;
        OrderRows.Clear();
        var seenMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in OrderedContentItems)
        {
            OrderRows.Add(new OverlayBlockOrderRowViewModel(group));
            var metrics = GetMetricsForItem(group).ToList();
            if (_metricOrder.TryGetValue(GetMetricOrderKey(group.Kind, group.Value), out var order))
            {
                var positions = order.Select((identity, index) => (identity, index))
                    .ToDictionary(pair => pair.identity, pair => pair.index, StringComparer.OrdinalIgnoreCase);
                metrics = metrics.OrderBy(metric => positions.TryGetValue(metric.Identity, out var index) ? index : int.MaxValue).ToList();
            }
            foreach (var metric in metrics)
                if (seenMetrics.Add(metric.Identity))
                    OrderRows.Add(new OverlayBlockOrderRowViewModel(group, metric));
        }
        if (selected is not null)
            SelectedOrderRow = OrderRows.FirstOrDefault(row => row.IsMetric == selected.IsMetric
                && row.GroupKey.Equals(selected.GroupKey, StringComparison.OrdinalIgnoreCase)
                && row.Identity.Equals(selected.Identity, StringComparison.OrdinalIgnoreCase));
        Raise(nameof(CanMoveContentUp));
        Raise(nameof(CanMoveContentDown));
        Raise(nameof(CanRemoveSelectedContent));
        Raise(nameof(RemoveSelectedContentText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private OverlayBlockContentChoiceViewModel CreateChoice(string kind, string value, string label, Action? selected = null)
        => CreateChoice(kind, value, label, selected, null);

    private OverlayBlockContentChoiceViewModel CreateChoice(string kind, string value, string label, Action<bool> selectionChanged)
        => CreateChoice(kind, value, label, null, selectionChanged);

    private OverlayBlockContentChoiceViewModel CreateChoice(string kind, string value, string label, Action? selected, Action<bool>? selectionChanged)
        => new(kind, value, label, _selected.Contains((kind, value)), isSelected =>
        {
            SetSelected(kind, value, isSelected);
            if (isSelected) selected?.Invoke();
            selectionChanged?.Invoke(isSelected);
        });

    private void SetSelected(string kind, string value, bool isSelected, bool publishDraft = true)
    {
        var key = (kind, value);
        if (isSelected)
        {
            if (!_selected.Add(key)) return;
            var added = new OverlayBlockItemChoiceViewModel(kind, value, GetItemLabel(kind, value));
            OrderedContentItems.Add(added);
            SelectedContentItem = added;
        }
        else
        {
            if (!_selected.Remove(key)) return;
            var item = OrderedContentItems.FirstOrDefault(candidate => candidate.Kind == kind
                && candidate.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                OrderedContentItems.Remove(item);
                if (ReferenceEquals(SelectedContentItem, item)) SelectedContentItem = null;
            }
        }
        Raise(nameof(CanMoveContentUp));
        Raise(nameof(CanMoveContentDown));
        RebuildOrderRows();
        if (publishDraft) PublishDraftToCanvas();
    }

    private OverlayBlockContentChoiceViewModel? FindChoice(string kind, string value)
    {
        IEnumerable<OverlayBlockContentChoiceViewModel> choices = CategoriesChoices
            .Concat([FrametimeChoice]).Concat(SeriesChoices).Concat(GraphChoices).Concat(TemplateChoices);
        return choices.FirstOrDefault(choice => choice.Kind == kind
            && choice.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private string GetItemLabel(string kind, string value) => kind switch
    {
        "category" => Categories.FirstOrDefault(item => item.Key == value).Name ?? value,
        "frametime" => "Gráfico de frametime",
        "series" => value,
        "graph" => _graphs.FirstOrDefault(item => item.Id == value)?.Series ?? "Gráfico",
        "template" => _templates.FirstOrDefault(item => item.Id == value)?.Text ?? "Template",
        _ => value
    };

    private static string SeriesCategory(string value)
    {
        if (SensorKey.TryParse(value, out var key))
            return CategoryFor(key.Type);

        var family = value.TrimEnd('*');
        if (family.StartsWith("Cpu", StringComparison.OrdinalIgnoreCase)) return "CPU";
        if (family.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase)) return "GPU";
        if (family.StartsWith("Ram", StringComparison.OrdinalIgnoreCase)) return "RAM";
        if (family.StartsWith("Disk", StringComparison.OrdinalIgnoreCase)) return "Disco";
        if (family.StartsWith("Network", StringComparison.OrdinalIgnoreCase)) return "Rede";
        if (family.StartsWith("Fan", StringComparison.OrdinalIgnoreCase)) return "Ventoinhas";
        if (family.StartsWith("Motherboard", StringComparison.OrdinalIgnoreCase)
            || family.StartsWith("Vrm", StringComparison.OrdinalIgnoreCase)
            || family.StartsWith("Rail", StringComparison.OrdinalIgnoreCase)) return "Placa-mãe";
        if (family.StartsWith("Battery", StringComparison.OrdinalIgnoreCase)) return "Bateria";
        if (family.StartsWith("Fps", StringComparison.OrdinalIgnoreCase)
            || family.StartsWith("Frametime", StringComparison.OrdinalIgnoreCase)
            || family.StartsWith("PresentLatency", StringComparison.OrdinalIgnoreCase)) return "FPS";
        return "Outros";
    }

    private static string CategoryFor(SensorType type) => type switch
    {
        SensorType.CpuTotal or SensorType.CpuPerCore or SensorType.CpuTemperature or SensorType.CpuPower
            or SensorType.CpuClock or SensorType.CpuVoltage or SensorType.CpuBusy or SensorType.CpuWait => "CPU",
        SensorType.GpuCore or SensorType.GpuMemory or SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal
            or SensorType.GpuTemperature or SensorType.GpuHotSpot or SensorType.GpuVramTemperature
            or SensorType.GpuCoreClock or SensorType.GpuMemoryClock or SensorType.GpuVoltage or SensorType.GpuFan
            or SensorType.GpuPower or SensorType.GpuBusy => "GPU",
        SensorType.RamUsage or SensorType.RamAvailable or SensorType.RamUsed or SensorType.RamTotal
            or SensorType.RamTemperature => "RAM",
        SensorType.DiskUsage or SensorType.DiskRead or SensorType.DiskWrite or SensorType.DiskTemperature
            or SensorType.DiskHealth or SensorType.DiskTotalRead or SensorType.DiskTotalWritten => "Disco",
        SensorType.NetworkUp or SensorType.NetworkDown or SensorType.NetworkUpTotal or SensorType.NetworkDownTotal => "Rede",
        SensorType.FanSpeed => "Ventoinhas",
        SensorType.MotherboardTemp or SensorType.RailVoltage or SensorType.VrmTemperature => "Placa-mãe",
        SensorType.BatteryLevel or SensorType.BatteryVoltage or SensorType.BatteryPower
            or SensorType.BatteryWearLevel or SensorType.BatteryAcPower => "Bateria",
        SensorType.Fps or SensorType.FrametimeMs or SensorType.FpsAvg or SensorType.FpsMin or SensorType.Fps1Low
            or SensorType.Fps01Low or SensorType.FpsMax or SensorType.PresentLatency or SensorType.FpsP1
            or SensorType.FpsP99 or SensorType.FrametimeP1 or SensorType.FrametimeP99 => "FPS",
        _ => "Outros"
    };

    private sealed class SeriesValueComparer : IComparer<string>
    {
        public static readonly SeriesValueComparer Instance = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            if (SensorKey.TryParse(left, out var leftKey) && SensorKey.TryParse(right, out var rightKey))
                return leftKey.CompareTo(rightKey);
            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }
    }

    private void CaptureSelection(IEnumerable<OverlayBlockContentChoiceViewModel> choices)
    {
        foreach (var choice in choices)
            SetSelected(choice.Kind, choice.Value, choice.IsSelected);
    }

    private static bool IsValidPattern(string value)
    {
        if (value == "*") return true;
        if (value.Count(character => character == '*') == 0)
            return SensorKey.TryParse(value, out var key) && Enum.IsDefined(key.Type);
        return value.Count(character => character == '*') == 1
            && (value.EndsWith('*') || value.StartsWith('*')) && value.Length > 1;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OverlayGraph Clone(OverlayGraph graph) => new()
    {
        Id = graph.Id, Series = graph.Series, Height = graph.Height, Window = graph.Window,
        Min = graph.Min, Max = graph.Max, Color = graph.Color
    };

    private static OverlayTemplate Clone(OverlayTemplate template) => new()
    {
        Id = template.Id, Text = template.Text, Color = template.Color
    };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
