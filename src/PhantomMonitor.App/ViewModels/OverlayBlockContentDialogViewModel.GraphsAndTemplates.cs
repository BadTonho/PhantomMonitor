using System.Globalization;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class OverlayBlockContentDialogViewModel
{
    public string GraphSeries { get => _graphSeries; set => Set(ref _graphSeries, value); }
    public string GraphHeight { get => _graphHeight; set => Set(ref _graphHeight, value); }
    public string GraphWindow { get => _graphWindow; set => Set(ref _graphWindow, value); }
    public string GraphMin { get => _graphMin; set => Set(ref _graphMin, value); }
    public string GraphMax { get => _graphMax; set => Set(ref _graphMax, value); }
    public string GraphColor { get => _graphColor; set => Set(ref _graphColor, value); }
    public string TemplateText { get => _templateText; set => Set(ref _templateText, value); }
    public string TemplateColor { get => _templateColor; set => Set(ref _templateColor, value); }

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
}

