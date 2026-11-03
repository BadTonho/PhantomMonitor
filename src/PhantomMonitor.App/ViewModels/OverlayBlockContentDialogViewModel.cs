using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

/// <summary>Draft state and editing operations for one overlay block's content dialog.</summary>
public sealed partial class OverlayBlockContentDialogViewModel : INotifyPropertyChanged
{
    private readonly OverlayCanvasBlockViewModel _block;
    private readonly MainWindowViewModel _owner;
    private readonly IReadOnlyList<string> _detectedSeries;
    private readonly HashSet<(string Kind, string Value)> _selected;
    private readonly List<OverlayGraph> _graphs;
    private readonly List<OverlayTemplate> _templates;
    private readonly List<OverlayCustomLineConfig> _customLines;
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
        _customLines = (block.Config.CustomLines ?? []).Select(MainWindowViewModel.CloneOverlayCustomLine).ToList();
        _owner.PropertyChanged += OnOwnerPropertyChanged;
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
        _customLines = source._customLines.Select(MainWindowViewModel.CloneOverlayCustomLine).ToList();
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

        foreach (var sourceOverride in source._metricOverrides.Values)
            _metricOverrides[sourceOverride.Identity] = Clone(sourceOverride);
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
    public ObservableCollection<OverlayBlockContentChoiceViewModel> SeriesChoices { get; } = [];
    public ObservableCollection<OverlayBlockContentChoiceViewModel> GraphChoices { get; } = [];
    public ObservableCollection<OverlayBlockContentChoiceViewModel> TemplateChoices { get; } = [];
    public ObservableCollection<OverlayBlockItemChoiceViewModel> OrderedContentItems { get; } = [];
    public ObservableCollection<OverlayBlockOrderRowViewModel> OrderRows { get; } = [];

    public IReadOnlyList<OverlayHiddenMetricOption> GetHiddenMetricOptions()
        => _owner.GetOverlayHiddenMetricOptions();

    public void RestoreHiddenMetrics(IEnumerable<string> identities)
    {
        if (!_owner.RestoreOverlayMetricsDraft(identities)) return;
        RefreshDynamicChoices();
        RebuildOrderRows();
    }

    public OverlayBlockContentDialogViewModel CreateContentPickerDraft() => new(this);

    public void Dispose()
    {
        _owner.OverlayRows.CollectionChanged -= OnOverlayRowsChanged;
        _owner.PropertyChanged -= OnOwnerPropertyChanged;
        foreach (var row in OrderRows) row.Dispose();
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
        _customLines.Clear();
        _customLines.AddRange(pickerDraft._customLines.Select(MainWindowViewModel.CloneOverlayCustomLine));

        OrderedContentItems.Clear();
        foreach (var item in pickerDraft.OrderedContentItems)
            OrderedContentItems.Add(new OverlayBlockItemChoiceViewModel(item.Kind, item.Value, GetItemLabel(item.Kind, item.Value)));
        _metricOverrides.Clear();
        foreach (var entry in pickerDraft._metricOverrides) _metricOverrides[entry.Key] = Clone(entry.Value);
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
        if (selectedItem is { Kind: "customline" })
            SelectedOrderRow = OrderRows.FirstOrDefault(row => row.Group.Kind == "customline" && row.Group.Value == selectedItem.Value);
        PublishDraftToCanvas();
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
            _metricOverrides.Values, metricOrder, _customLines);
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
        "customline" => CustomLineLabel(value),
        _ => value
    };

    private void CaptureSelection(IEnumerable<OverlayBlockContentChoiceViewModel> choices)
    {
        foreach (var choice in choices)
            SetSelected(choice.Kind, choice.Value, choice.IsSelected);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
