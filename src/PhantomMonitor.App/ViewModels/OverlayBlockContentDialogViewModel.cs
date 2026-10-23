using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed class OverlayBlockContentChoiceViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private readonly Action<bool>? _selectionChanged;

    public OverlayBlockContentChoiceViewModel(string kind, string value, string label, bool isSelected,
        Action<bool>? selectionChanged = null)
    {
        Kind = kind;
        Value = value;
        Label = label;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    public string Kind { get; }
    public string Value { get; }
    public string Label { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            _selectionChanged?.Invoke(value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Draft state and editing operations for one overlay block's content dialog.</summary>
public sealed class OverlayBlockContentDialogViewModel : INotifyPropertyChanged
{
    private static readonly (string Key, string Name)[] Categories =
    [
        ("cpu", "CPU"), ("gpu", "GPU"), ("ram", "RAM"), ("disk", "Disco"),
        ("network", "Rede"), ("fans", "Ventoinhas"), ("motherboard", "Placa-mãe"), ("fps", "FPS / frametime")
    ];

    private readonly OverlayCanvasBlockViewModel _block;
    private readonly IReadOnlyList<string> _detectedSeries;
    private readonly HashSet<(string Kind, string Value)> _selected;
    private readonly List<OverlayGraph> _graphs;
    private readonly List<OverlayTemplate> _templates;
    private string? _editingGraphId;
    private string? _editingTemplateId;
    private string _seriesPatternInput = "";
    private string _graphSeries = "";
    private string _graphHeight = "36";
    private string _graphWindow = "240";
    private string _graphMin = "";
    private string _graphMax = "";
    private string _graphColor = "";
    private string _templateText = "";
    private string _templateColor = "";

    public OverlayBlockContentDialogViewModel(MainWindowViewModel viewModel, OverlayCanvasBlockViewModel block)
    {
        _block = block;
        _detectedSeries = viewModel.OverlaySeriesOptions.ToArray();
        _graphs = viewModel.OverlayGraphsDraft.Select(Clone).ToList();
        _templates = viewModel.OverlayTemplatesDraft.Select(Clone).ToList();
        _selected = block.Config.Items.Select(item => (item.Kind, item.Value)).ToHashSet();

        foreach (var (key, name) in Categories)
            CategoriesChoices.Add(CreateChoice("category", key, name));
        FrametimeChoice = CreateChoice("frametime", "frametime", "Gráfico de frametime");
        RefreshDynamicChoices();
    }

    public ObservableCollection<OverlayBlockContentChoiceViewModel> CategoriesChoices { get; } = [];
    public OverlayBlockContentChoiceViewModel FrametimeChoice { get; }
    public ObservableCollection<OverlayBlockContentChoiceViewModel> SeriesChoices { get; } = [];
    public ObservableCollection<OverlayBlockContentChoiceViewModel> GraphChoices { get; } = [];
    public ObservableCollection<OverlayBlockContentChoiceViewModel> TemplateChoices { get; } = [];
    public IReadOnlyList<OverlayBlockItemConfig> Result { get; private set; } = [];
    public IReadOnlyList<OverlayGraph> ResultGraphs => _graphs;
    public IReadOnlyList<OverlayTemplate> ResultTemplates => _templates;

    public string SeriesPatternInput { get => _seriesPatternInput; set => Set(ref _seriesPatternInput, value); }
    public string GraphSeries { get => _graphSeries; set => Set(ref _graphSeries, value); }
    public string GraphHeight { get => _graphHeight; set => Set(ref _graphHeight, value); }
    public string GraphWindow { get => _graphWindow; set => Set(ref _graphWindow, value); }
    public string GraphMin { get => _graphMin; set => Set(ref _graphMin, value); }
    public string GraphMax { get => _graphMax; set => Set(ref _graphMax, value); }
    public string GraphColor { get => _graphColor; set => Set(ref _graphColor, value); }
    public string TemplateText { get => _templateText; set => Set(ref _templateText, value); }
    public string TemplateColor { get => _templateColor; set => Set(ref _templateColor, value); }

    public void AddSeriesPattern()
    {
        var value = SeriesPatternInput.Trim();
        if (!IsValidPattern(value) || _selected.Contains(("series", value))) return;
        _selected.Add(("series", value));
        SeriesPatternInput = "";
        RefreshDynamicChoices();
    }

    public void RefreshDynamicChoices()
    {
        CaptureSelection(SeriesChoices);
        CaptureSelection(GraphChoices);
        CaptureSelection(TemplateChoices);

        SeriesChoices.Clear();
        var series = _detectedSeries
            .Concat(_block.Config.Items.Where(item => item.Kind == "series").Select(item => item.Value))
            .Concat(_selected.Where(item => item.Kind == "series").Select(item => item.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        foreach (var value in series)
            SeriesChoices.Add(CreateChoice("series", value, value));

        GraphChoices.Clear();
        foreach (var graph in _graphs)
            GraphChoices.Add(CreateChoice("graph", graph.Id, graph.Series, () => _editingGraphId = graph.Id));

        TemplateChoices.Clear();
        foreach (var template in _templates)
            TemplateChoices.Add(CreateChoice("template", template.Id, template.Text, () => _editingTemplateId = template.Id));
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
        _selected.Add(("graph", graph.Id));
        _editingGraphId = graph.Id;
        RefreshDynamicChoices();
    }

    public void RemoveGraph()
    {
        if (_editingGraphId is null) return;
        _graphs.RemoveAll(item => item.Id == _editingGraphId);
        _selected.Remove(("graph", _editingGraphId));
        NewGraph();
        RefreshDynamicChoices();
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
        _selected.Add(("template", template.Id));
        _editingTemplateId = template.Id;
        RefreshDynamicChoices();
    }

    public void RemoveTemplate()
    {
        if (_editingTemplateId is null) return;
        _templates.RemoveAll(item => item.Id == _editingTemplateId);
        _selected.Remove(("template", _editingTemplateId));
        NewTemplate();
        RefreshDynamicChoices();
    }

    public void Apply()
    {
        CaptureSelection(CategoriesChoices);
        CaptureSelection([FrametimeChoice]);
        CaptureSelection(SeriesChoices);
        CaptureSelection(GraphChoices);
        CaptureSelection(TemplateChoices);
        Result = Categories.Select(item => ("category", item.Key))
            .Append(("frametime", "frametime"))
            .Concat(_detectedSeries.Concat(_block.Config.Items.Where(item => item.Kind == "series").Select(item => item.Value))
                .Concat(_selected.Where(item => item.Kind == "series").Select(item => item.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Select(value => ("series", value)))
            .Concat(_graphs.Select(graph => ("graph", graph.Id)))
            .Concat(_templates.Select(template => ("template", template.Id)))
            .Where(_selected.Contains)
            .Select(item => new OverlayBlockItemConfig { Kind = item.Item1, Value = item.Item2 })
            .ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private OverlayBlockContentChoiceViewModel CreateChoice(string kind, string value, string label, Action? selected = null)
        => new(kind, value, label, _selected.Contains((kind, value)), isSelected =>
        {
            if (isSelected) _selected.Add((kind, value));
            else _selected.Remove((kind, value));
            if (isSelected) selected?.Invoke();
        });

    private void CaptureSelection(IEnumerable<OverlayBlockContentChoiceViewModel> choices)
    {
        foreach (var choice in choices)
        {
            if (choice.IsSelected) _selected.Add((choice.Kind, choice.Value));
            else _selected.Remove((choice.Kind, choice.Value));
        }
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
}
