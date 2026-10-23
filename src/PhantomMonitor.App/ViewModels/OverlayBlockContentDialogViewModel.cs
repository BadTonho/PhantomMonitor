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
    private const string AllSeriesCategories = "Todas";

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
    private string _selectedSeriesCategory = AllSeriesCategories;
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
    public IReadOnlyList<string> SeriesCategories { get; } =
    [
        AllSeriesCategories, "CPU", "GPU", "RAM", "Disco", "Rede", "Ventoinhas",
        "Placa-mãe", "Bateria", "FPS", "Outros"
    ];
    public ObservableCollection<OverlayBlockContentChoiceViewModel> SeriesChoices { get; } = [];
    public ObservableCollection<OverlayBlockContentChoiceViewModel> GraphChoices { get; } = [];
    public ObservableCollection<OverlayBlockContentChoiceViewModel> TemplateChoices { get; } = [];
    public IReadOnlyList<OverlayBlockItemConfig> Result { get; private set; } = [];
    public IReadOnlyList<OverlayGraph> ResultGraphs => _graphs;
    public IReadOnlyList<OverlayTemplate> ResultTemplates => _templates;

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

        RefreshSeriesChoices();

        GraphChoices.Clear();
        foreach (var graph in _graphs)
            GraphChoices.Add(CreateChoice("graph", graph.Id, graph.Series, () => _editingGraphId = graph.Id));

        TemplateChoices.Clear();
        foreach (var template in _templates)
            TemplateChoices.Add(CreateChoice("template", template.Id, template.Text, () => _editingTemplateId = template.Id));
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
        => CreateChoice(kind, value, label, selected, null);

    private OverlayBlockContentChoiceViewModel CreateChoice(string kind, string value, string label, Action<bool> selectionChanged)
        => CreateChoice(kind, value, label, null, selectionChanged);

    private OverlayBlockContentChoiceViewModel CreateChoice(string kind, string value, string label, Action? selected, Action<bool>? selectionChanged)
        => new(kind, value, label, _selected.Contains((kind, value)), isSelected =>
        {
            if (isSelected) _selected.Add((kind, value));
            else _selected.Remove((kind, value));
            if (isSelected) selected?.Invoke();
            selectionChanged?.Invoke(isSelected);
        });

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

    private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
