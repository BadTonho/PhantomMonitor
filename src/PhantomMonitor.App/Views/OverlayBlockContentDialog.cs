using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System.Globalization;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

/// <summary>Seletor multi-item e editor das definições de gráfico/template de um bloco.</summary>
public sealed class OverlayBlockContentDialog : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly OverlayCanvasBlockViewModel _block;
    private readonly List<OverlayGraph> _graphs;
    private readonly List<OverlayTemplate> _templates;
    private readonly HashSet<(string Kind, string Value)> _selected;
    private readonly List<CheckBox> _contentChecks = [];
    private readonly StackPanel _seriesChoices = new() { Spacing = 3 };
    private readonly StackPanel _graphChoices = new() { Spacing = 3 };
    private readonly StackPanel _templateChoices = new() { Spacing = 3 };
    private readonly TextBox _seriesPattern = new() { PlaceholderText = "Série ou padrão (ex.: Gpu* ou DiskRead#1)" };
    private readonly TextBox _graphSeries = new() { PlaceholderText = "Série (ex.: GpuTemperature#1)" };
    private readonly TextBox _graphHeight = new() { Text = "36", Width = 90 };
    private readonly TextBox _graphWindow = new() { Text = "240", Width = 90 };
    private readonly TextBox _graphMin = new() { PlaceholderText = "Auto", Width = 90 };
    private readonly TextBox _graphMax = new() { PlaceholderText = "Auto", Width = 90 };
    private readonly TextBox _graphColor = new() { PlaceholderText = "Cor opcional #RRGGBB" };
    private readonly TextBox _templateText = new() { PlaceholderText = "Texto com placeholders, ex.: GPU {GpuTemperature:F0} °C" };
    private readonly TextBox _templateColor = new() { PlaceholderText = "Cor opcional #RRGGBB" };
    private string? _editingGraphId;
    private string? _editingTemplateId;

    public IReadOnlyList<OverlayBlockItemConfig> Result { get; private set; } = [];
    public IReadOnlyList<OverlayGraph> ResultGraphs => _graphs;
    public IReadOnlyList<OverlayTemplate> ResultTemplates => _templates;

    public OverlayBlockContentDialog(MainWindowViewModel viewModel, OverlayCanvasBlockViewModel block)
    {
        _viewModel = viewModel;
        _block = block;
        _graphs = viewModel.OverlayGraphsDraft.Select(Clone).ToList();
        _templates = viewModel.OverlayTemplatesDraft.Select(Clone).ToList();
        _selected = block.Config.Items.Select(item => (item.Kind, item.Value)).ToHashSet();
        Title = "Conteúdo do bloco";
        Width = 700;
        Height = 760;
        MinWidth = 560;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildContent();
        RefreshDynamicChoices();
    }

    private Control BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        root.Children.Add(new TextBlock { Text = "CONTEÚDO DO BLOCO", FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        root.Children.Add(new TextBlock { Text = "Marque vários itens para reuni-los neste bloco.", FontSize = 11, TextWrapping = TextWrapping.Wrap });

        root.Children.Add(SectionTitle("CATEGORIAS"));
        var categories = new UniformGrid { Columns = 2 };
        foreach (var (key, name) in Categories)
            categories.Children.Add(AddChoice("category", key, name));
        root.Children.Add(categories);
        root.Children.Add(AddChoice("frametime", "frametime", "Gráfico de frametime"));

        root.Children.Add(SectionTitle("SÉRIES"));
        root.Children.Add(_seriesChoices);
        var patternRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 8 };
        patternRow.Children.Add(_seriesPattern);
        var addPattern = new Button { Content = "Adicionar padrão" };
        Grid.SetColumn(addPattern, 1);
        addPattern.Click += (_, _) => AddSeriesPattern();
        patternRow.Children.Add(addPattern);
        root.Children.Add(patternRow);

        root.Children.Add(SectionTitle("GRÁFICOS"));
        root.Children.Add(_graphChoices);
        root.Children.Add(_graphSeries);
        var graphOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        graphOptions.Children.Add(new StackPanel { Children = { new TextBlock { Text = "ALTURA" }, _graphHeight } });
        graphOptions.Children.Add(new StackPanel { Children = { new TextBlock { Text = "AMOSTRAS" }, _graphWindow } });
        graphOptions.Children.Add(new StackPanel { Children = { new TextBlock { Text = "MÍN." }, _graphMin } });
        graphOptions.Children.Add(new StackPanel { Children = { new TextBlock { Text = "MÁX." }, _graphMax } });
        root.Children.Add(graphOptions);
        root.Children.Add(_graphColor);
        var graphButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        graphButtons.Children.Add(ActionButton("Novo gráfico", (_, _) => NewGraph()));
        graphButtons.Children.Add(ActionButton("Salvar gráfico", (_, _) => SaveGraph()));
        graphButtons.Children.Add(ActionButton("Remover selecionado", (_, _) => RemoveGraph()));
        root.Children.Add(graphButtons);

        root.Children.Add(SectionTitle("TEMPLATES"));
        root.Children.Add(_templateChoices);
        root.Children.Add(_templateText);
        root.Children.Add(_templateColor);
        var templateButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        templateButtons.Children.Add(ActionButton("Novo template", (_, _) => NewTemplate()));
        templateButtons.Children.Add(ActionButton("Salvar template", (_, _) => SaveTemplate()));
        templateButtons.Children.Add(ActionButton("Remover selecionado", (_, _) => RemoveTemplate()));
        root.Children.Add(templateButtons);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        footer.Children.Add(ActionButton("Cancelar", (_, _) => Close(false)));
        footer.Children.Add(ActionButton("Aplicar conteúdo", (_, _) => ApplyAndClose(footer)));
        root.Children.Add(footer);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root
        };
    }

    private CheckBox AddChoice(string kind, string value, string label)
    {
        var check = new CheckBox { Content = label, IsChecked = _selected.Contains((kind, value)) };
        check.Tag = (kind, value);
        _contentChecks.Add(check);
        return check;
    }

    private void RefreshDynamicChoices()
    {
        foreach (var check in _contentChecks)
        {
            if (check.Tag is ValueTuple<string, string> item)
            {
                if (check.IsChecked == true) _selected.Add(item);
                else _selected.Remove(item);
            }
        }
        _seriesChoices.Children.Clear();
        _contentChecks.RemoveAll(check => check.Tag is ValueTuple<string, string> tuple && tuple.Item1 == "series");
        var series = _viewModel.OverlaySeriesOptions
            .Concat(_block.Config.Items.Where(item => item.Kind == "series").Select(item => item.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        foreach (var value in series) _seriesChoices.Children.Add(AddChoice("series", value, value));

        _graphChoices.Children.Clear();
        _contentChecks.RemoveAll(check => check.Tag is ValueTuple<string, string> tuple && tuple.Item1 == "graph");
        foreach (var graph in _graphs)
        {
            var check = AddChoice("graph", graph.Id, graph.Series);
            check.IsCheckedChanged += (_, _) => _editingGraphId = graph.Id;
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(check);
            var edit = ActionButton("Editar", (_, _) => EditGraph(graph));
            Grid.SetColumn(edit, 1);
            row.Children.Add(edit);
            _graphChoices.Children.Add(row);
        }

        _templateChoices.Children.Clear();
        _contentChecks.RemoveAll(check => check.Tag is ValueTuple<string, string> tuple && tuple.Item1 == "template");
        foreach (var template in _templates)
        {
            var check = AddChoice("template", template.Id, template.Text);
            check.IsCheckedChanged += (_, _) => _editingTemplateId = template.Id;
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(check);
            var edit = ActionButton("Editar", (_, _) => EditTemplate(template));
            Grid.SetColumn(edit, 1);
            row.Children.Add(edit);
            _templateChoices.Children.Add(row);
        }
    }

    private void AddSeriesPattern()
    {
        var value = _seriesPattern.Text?.Trim() ?? "";
        if (!IsValidPattern(value) || _contentChecks.Any(check => check.Tag is ValueTuple<string, string> item
                && item.Item1 == "series" && item.Item2.Equals(value, StringComparison.OrdinalIgnoreCase))) return;
        _selected.Add(("series", value));
        _seriesPattern.Text = "";
        RefreshDynamicChoices();
    }

    private static bool IsValidPattern(string value)
    {
        if (value == "*") return true;
        if (value.Count(character => character == '*') == 0)
            return SensorKey.TryParse(value, out var key) && Enum.IsDefined(key.Type);
        return value.Count(character => character == '*') == 1
            && (value.EndsWith('*') || value.StartsWith('*')) && value.Length > 1;
    }

    private void NewGraph()
    {
        _editingGraphId = null;
        _graphSeries.Text = "";
        _graphHeight.Text = "36";
        _graphWindow.Text = "240";
        _graphMin.Text = "";
        _graphMax.Text = "";
        _graphColor.Text = "";
    }

    private void EditGraph(OverlayGraph graph)
    {
        _editingGraphId = graph.Id;
        _graphSeries.Text = graph.Series;
        _graphHeight.Text = graph.Height.ToString(CultureInfo.InvariantCulture);
        _graphWindow.Text = graph.Window.ToString(CultureInfo.InvariantCulture);
        _graphMin.Text = graph.Min?.ToString(CultureInfo.InvariantCulture) ?? "";
        _graphMax.Text = graph.Max?.ToString(CultureInfo.InvariantCulture) ?? "";
        _graphColor.Text = graph.Color ?? "";
    }

    private void SaveGraph()
    {
        var series = _graphSeries.Text?.Trim() ?? "";
        if (series.Length == 0 || !double.TryParse(_graphHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            || !int.TryParse(_graphWindow.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var window)) return;
        double? min = double.TryParse(_graphMin.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minValue) ? minValue : null;
        double? max = double.TryParse(_graphMax.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxValue) ? maxValue : null;
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
        graph.Color = EmptyToNull(_graphColor.Text);
        _selected.Add(("graph", graph.Id));
        _editingGraphId = graph.Id;
        RefreshDynamicChoices();
    }

    private void RemoveGraph()
    {
        if (_editingGraphId is null) return;
        _graphs.RemoveAll(item => item.Id == _editingGraphId);
        _selected.Remove(("graph", _editingGraphId));
        NewGraph();
        RefreshDynamicChoices();
    }

    private void NewTemplate()
    {
        _editingTemplateId = null;
        _templateText.Text = "";
        _templateColor.Text = "";
    }

    private void EditTemplate(OverlayTemplate template)
    {
        _editingTemplateId = template.Id;
        _templateText.Text = template.Text;
        _templateColor.Text = template.Color ?? "";
    }

    private void SaveTemplate()
    {
        var text = _templateText.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        var template = _templates.FirstOrDefault(item => item.Id == _editingTemplateId);
        if (template is null)
        {
            template = new OverlayTemplate { Id = Guid.NewGuid().ToString("N") };
            _templates.Add(template);
        }
        template.Text = text;
        template.Color = EmptyToNull(_templateColor.Text);
        _selected.Add(("template", template.Id));
        _editingTemplateId = template.Id;
        RefreshDynamicChoices();
    }

    private void RemoveTemplate()
    {
        if (_editingTemplateId is null) return;
        _templates.RemoveAll(item => item.Id == _editingTemplateId);
        _selected.Remove(("template", _editingTemplateId));
        NewTemplate();
        RefreshDynamicChoices();
    }

    private void ApplyAndClose(StackPanel footer)
    {
        var selected = _contentChecks.Where(check => check.IsChecked == true)
            .Select(check => ((string Kind, string Value))check.Tag!)
            .ToHashSet();
        Result = Categories.Select(item => ("category", item.Key))
            .Append(("frametime", "frametime"))
            .Concat(_viewModel.OverlaySeriesOptions.Concat(_block.Config.Items.Where(item => item.Kind == "series").Select(item => item.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Select(value => ("series", value)))
            .Concat(_graphs.Select(graph => ("graph", graph.Id)))
            .Concat(_templates.Select(template => ("template", template.Id)))
            .Where(selected.Contains)
            .Select(item => new OverlayBlockItemConfig { Kind = item.Item1, Value = item.Item2 })
            .ToList();
        Close(true);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static TextBlock SectionTitle(string text) => new() { Text = text, FontSize = 10, FontWeight = Avalonia.Media.FontWeight.Bold };
    private static Button ActionButton(string text, EventHandler<RoutedEventArgs> handler)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5) };
        button.Click += handler;
        return button;
    }
    private static OverlayGraph Clone(OverlayGraph graph) => new()
    {
        Id = graph.Id, Series = graph.Series, Height = graph.Height, Window = graph.Window,
        Min = graph.Min, Max = graph.Max, Color = graph.Color
    };
    private static OverlayTemplate Clone(OverlayTemplate template) => new() { Id = template.Id, Text = template.Text, Color = template.Color };

    private static readonly (string Key, string Name)[] Categories =
    [
        ("cpu", "CPU"), ("gpu", "GPU"), ("ram", "RAM"), ("disk", "Disco"),
        ("network", "Rede"), ("fans", "Ventoinhas"), ("motherboard", "Placa-mãe"), ("fps", "FPS / frametime")
    ];
}
