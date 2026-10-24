using System.Collections.ObjectModel;
using System.Globalization;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class OverlayBlockContentDialogViewModel
{
    private readonly Dictionary<string, OverlayMetricOverrideConfig> _metricOverrides = new(StringComparer.OrdinalIgnoreCase);
    private OverlayMetricEditChoiceViewModel? _selectedMetric;
    private string _metricDisplayName = "";
    private string _metricDecimals = "Automática";
    private string _metricColor = "";
    private string _metricEditorMessage = "";

    public ObservableCollection<OverlayMetricEditChoiceViewModel> MetricChoices { get; } = [];
    public IReadOnlyList<OverlayMetricOverrideConfig> ResultMetricOverrides { get; private set; } = [];
    public IReadOnlyList<OverlayColorSwatch> ColorSwatches => _owner.OverlayColorSwatches;
    public IReadOnlyList<string> MetricDecimalsOptions { get; } = ["Automática", "0", "1", "2", "3"];
    public bool CanEditMetricDecimals => SelectedMetric?.SupportsPrecision == true;
    public bool HasMetricChoices => MetricChoices.Count > 0;
    public bool NoMetricChoices => SelectedContentItem is { Kind: "category" or "series" } && !HasMetricChoices;

    public OverlayMetricEditChoiceViewModel? SelectedMetric
    {
        get => _selectedMetric;
        set
        {
            if (!Set(ref _selectedMetric, value)) return;
            LoadMetricOverrideDraft();
        }
    }

    public string MetricDisplayName { get => _metricDisplayName; set => Set(ref _metricDisplayName, value ?? ""); }
    public string MetricDecimals
    {
        get => _metricDecimals;
        set
        {
            if (Set(ref _metricDecimals, value ?? "Automática"))
                Raise(nameof(CanEditMetricDecimals));
        }
    }
    public string MetricColor { get => _metricColor; set => Set(ref _metricColor, value ?? ""); }
    public string MetricEditorMessage { get => _metricEditorMessage; private set => Set(ref _metricEditorMessage, value); }

    public void SaveMetricOverride()
    {
        MetricEditorMessage = "";
        if (SelectedMetric is not { } selected) return;
        var displayName = string.IsNullOrWhiteSpace(MetricDisplayName) ? null : MetricDisplayName.Trim();
        if (displayName is { Length: > 128 } || displayName?.Any(char.IsControl) == true)
        {
            MetricEditorMessage = "O nome deve ter até 128 caracteres.";
            return;
        }

        int? decimals = null;
        if (CanEditMetricDecimals && MetricDecimals != "Automática"
            && int.TryParse(MetricDecimals, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            decimals = Math.Clamp(parsed, 0, 3);

        var color = MetricColor.Trim();
        if (color.Length > 0
            && (color.Length != 7 || !OverlayBrushHelper.TryParseColor(color, out _)))
        {
            MetricEditorMessage = "Informe a cor no formato #RRGGBB.";
            return;
        }
        color = color.Length == 0 ? null : color.ToUpperInvariant();

        if (displayName is null && decimals is null && color is null)
            _metricOverrides.Remove(selected.Identity);
        else
            _metricOverrides[selected.Identity] = new OverlayMetricOverrideConfig
            {
                Identity = selected.Identity,
                DisplayName = displayName,
                Decimals = decimals,
                Color = color
            };
        RefreshBlockPreview();
        Raise(nameof(PreviewTitle));
        LoadMetricOverrideDraft();
    }

    public void SetMetricColor(string color) => MetricColor = color;
    public void ResetMetricColor() => MetricColor = "";

    private void RefreshMetricChoices()
    {
        if (SelectedContentItem is { Kind: "category" or "series" } item)
            RefreshMetricChoicesFor(item);
        else
            MetricChoices.Clear();
        Raise(nameof(HasMetricChoices));
        Raise(nameof(NoMetricChoices));
        SelectedMetric = null;
    }

    private void RefreshMetricChoicesFor(OverlayBlockItemChoiceViewModel item)
    {
        MetricChoices.Clear();
        foreach (var choice in GetMetricsForItem(item))
            MetricChoices.Add(choice);
        Raise(nameof(HasMetricChoices));
        Raise(nameof(NoMetricChoices));
    }

    private IEnumerable<OverlayMetricEditChoiceViewModel> GetMetricsForItem(OverlayBlockItemChoiceViewModel item)
    {
        IEnumerable<OverlayMetricViewModel> rows = item.Kind switch
        {
            "category" => _owner.OverlayRows.Where(row => !row.IsIndividualSeries
                && OverlayBlockKeys.ForIdentity(row.Identity).Equals(item.Value, StringComparison.OrdinalIgnoreCase)),
            "series" => _owner.OverlayRows.Where(row => row.IsIndividualSeries && row.SeriesKey is { } key
                && (key.IsPrimary
                    ? !item.Value.Contains('*') && key.MatchesPattern(item.Value)
                    : key.MatchesPattern(item.Value))),
            _ => []
        };
        return rows.Select(row =>
        {
            if (_metricEditChoices.TryGetValue(row.OverrideIdentity, out var existing)
                && ReferenceEquals(existing.Source, row))
                return existing;
            existing?.Dispose();
            var choice = new OverlayMetricEditChoiceViewModel(row);
            _metricEditChoices[row.OverrideIdentity] = choice;
            return choice;
        });
    }

    private void LoadMetricOverrideDraft()
    {
        MetricEditorMessage = "";
        var metricOverride = SelectedMetric is { } selected
            && _metricOverrides.TryGetValue(selected.Identity, out var value) ? value : null;
        MetricDisplayName = metricOverride?.DisplayName ?? "";
        MetricDecimals = metricOverride?.Decimals?.ToString(CultureInfo.InvariantCulture) ?? "Automática";
        MetricColor = metricOverride?.Color ?? "";
        Raise(nameof(CanEditMetricDecimals));
    }

    private static OverlayMetricOverrideConfig Clone(OverlayMetricOverrideConfig item) => new()
    {
        Identity = item.Identity, DisplayName = item.DisplayName, Decimals = item.Decimals, Color = item.Color
    };
}
