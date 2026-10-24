using System.Globalization;
using Avalonia.Media;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private double _overlayBackgroundOpacityPercent = 100;
    private string _overlayBackgroundColorText = "";
    private bool _overlayBorderEnabled = true;
    private string _overlayBorderColorText = "";
    private string _overlayFontFamilySelection = "Padrão do app";
    private string _overlayFontFamilyCustomText = "";
    private IReadOnlyList<string> _overlayFontFamilyOptions = ["Padrão do app"];
    private IBrush _overlayBackgroundBrush = ResolveBrush("BgOverlayPanelBrush");
    private FontFamily? _overlayFontFamilyEffective;
    private static readonly FontFamily DefaultOverlayValueFontFamily = new("monospace");

    public double OverlayBackgroundOpacityPercent
    {
        get => _overlayBackgroundOpacityPercent;
        set
        {
            if (!Set(ref _overlayBackgroundOpacityPercent, value)) return;
            Raise(nameof(OverlayBackgroundOpacityLabel));
            RaiseOverlayPreviewProperties();
        }
    }

    public string OverlayBackgroundOpacityLabel => $"{OverlayBackgroundOpacityPercent:0}%";

    public string OverlayBackgroundColorText
    {
        get => _overlayBackgroundColorText;
        set
        {
            if (Set(ref _overlayBackgroundColorText, value ?? ""))
                RaiseOverlayPreviewProperties();
        }
    }

    public bool OverlayBorderEnabled
    {
        get => _overlayBorderEnabled;
        set
        {
            if (Set(ref _overlayBorderEnabled, value))
                RaiseOverlayPreviewProperties();
        }
    }

    public string OverlayBorderColorText
    {
        get => _overlayBorderColorText;
        set
        {
            if (Set(ref _overlayBorderColorText, value ?? ""))
                RaiseOverlayPreviewProperties();
        }
    }

    public IReadOnlyList<OverlayColorSwatch> OverlayColorSwatches { get; } = CreateOverlayColorSwatches();

    public IReadOnlyList<string> OverlayFontFamilyOptions
    {
        get => _overlayFontFamilyOptions;
        private set => Set(ref _overlayFontFamilyOptions, value);
    }

    public string OverlayFontFamilySelection
    {
        get => _overlayFontFamilySelection;
        set
        {
            if (!Set(ref _overlayFontFamilySelection, value ?? "Padrão do app")) return;
            if (_overlayFontFamilyCustomText.Length > 0)
                Set(ref _overlayFontFamilyCustomText, "", nameof(OverlayFontFamilyCustomText));
            RaiseOverlayPreviewProperties();
        }
    }

    public string OverlayFontFamilyCustomText
    {
        get => _overlayFontFamilyCustomText;
        set
        {
            if (!Set(ref _overlayFontFamilyCustomText, value ?? "")) return;
            if (!string.IsNullOrWhiteSpace(_overlayFontFamilyCustomText)
                && !string.Equals(_overlayFontFamilySelection, "Padrão do app", StringComparison.Ordinal))
                Set(ref _overlayFontFamilySelection, "Padrão do app", nameof(OverlayFontFamilySelection));
            RaiseOverlayPreviewProperties();
        }
    }

    public IBrush OverlayBackgroundBrush => _overlayBackgroundBrush;
    public Avalonia.Thickness OverlayBorderThickness => _config.Overlay.BorderEnabled ? new(1) : new(0);
    public FontFamily OverlayMetricNameFontFamily => _overlayFontFamilyEffective ?? FontFamily.Default;
    public FontFamily OverlayMetricValueFontFamily => _overlayFontFamilyEffective ?? DefaultOverlayValueFontFamily;

    public IBrush OverlayPreviewBackgroundBrush => CreateOverlayBackgroundBrush(
        OverlayBackgroundColorText, OverlayBackgroundOpacityPercent / 100.0);
    public IBrush OverlayPreviewBorderBrush => OverlayBorderEnabled
        ? OverlayBrushHelper.Resolve(OverlayBorderColorText, ResolveBrush("OverlayBorderBrush"))
        : Brushes.Transparent;
    public Avalonia.Thickness OverlayPreviewBorderThickness => OverlayBorderEnabled ? new(1) : new(0);
    public FontFamily OverlayPreviewNameFontFamily => ResolvePreviewFontFamily() ?? FontFamily.Default;
    public FontFamily OverlayPreviewValueFontFamily => ResolvePreviewFontFamily() ?? DefaultOverlayValueFontFamily;

    private string OverlayFontFamilyDraftValue => !string.IsNullOrWhiteSpace(OverlayFontFamilyCustomText)
        ? OverlayFontFamilyCustomText.Trim()
        : string.Equals(OverlayFontFamilySelection, "Padrão do app", StringComparison.Ordinal)
            ? ""
            : OverlayFontFamilySelection.Trim();

    private void RaiseOverlayPreviewProperties()
    {
        Raise(nameof(OverlayPreviewWidth));
        Raise(nameof(OverlayPreviewWidthInvalid));
        Raise(nameof(OverlayPreviewOpacity));
        Raise(nameof(OverlayPreviewBackgroundBrush));
        Raise(nameof(OverlayPreviewBorderBrush));
        Raise(nameof(OverlayPreviewBorderThickness));
        Raise(nameof(OverlayPreviewNameFontFamily));
        Raise(nameof(OverlayPreviewValueFontFamily));
        Raise(nameof(OverlayPreviewNameFontSize));
        Raise(nameof(OverlayPreviewValueFontSize));
        Raise(nameof(OverlayPreviewSecondaryFontSize));
        Raise(nameof(OverlayPreviewProfileTagFontSize));
        Raise(nameof(OverlayPreviewContentSpacing));
        Raise(nameof(OverlayPreviewRowMargin));
        Raise(nameof(OverlayPreviewOuterMargin));
        Raise(nameof(OverlayPreviewPadding));
        Raise(nameof(OverlayPreviewFrametimeGraphHeight));
        Raise(nameof(OverlayEditorCanvasWidth));
        Raise(nameof(OverlayEditorCanvasHeight));
        foreach (var block in _overlayBlocks)
        {
            block.RefreshPresentation();
            block.RefreshCanvasPosition();
        }
    }

    private FontFamily? ResolvePreviewFontFamily()
    {
        var name = OverlayFontFamilyDraftValue;
        if (name.Length == 0) return null;
        try { return new FontFamily(name); }
        catch { return null; }
    }

    private static IReadOnlyList<OverlayColorSwatch> CreateOverlayColorSwatches()
    {
        string[] colors =
        [
            "#0B0F14", "#1E293B", "#243345", "#334155", "#475569", "#64748B",
            "#7F1D1D", "#DC2626", "#EA580C", "#EAB308", "#16A34A", "#0D9488",
            "#0284C7", "#2563EB", "#7C3AED", "#C026D3", "#F8FAFC"
        ];
        return colors.Select(hex => new OverlayColorSwatch(hex, new SolidColorBrush(Color.Parse(hex)))).ToArray();
    }

    private static IReadOnlyList<string> LoadOverlayFontFamilyOptions()
    {
        try
        {
            return ["Padrão do app", .. FontManager.Current.SystemFonts
                .Select(font => font.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch
        {
            return ["Padrão do app"];
        }
    }

    private void LoadOverlayAppearanceDraft()
    {
        _overlayBackgroundOpacityPercent = _config.Overlay.BackgroundOpacity * 100;
        _overlayBackgroundColorText = _config.Overlay.BackgroundColor ?? "";
        _overlayBorderEnabled = _config.Overlay.BorderEnabled;
        _overlayBorderColorText = _config.Overlay.BorderColor ?? "";
        SetOverlayFontFamilyDraft(_config.Overlay.FontFamily);
    }

    public void DiscardOverlayAppearanceDraft()
    {
        OverlayWidthText = _config.Overlay.Width.ToString(CultureInfo.CurrentCulture);
        OverlayOpacityPercent = _config.OverlayOpacity * 100;
        OverlayBackgroundOpacityPercent = _config.Overlay.BackgroundOpacity * 100;
        OverlayBackgroundColorText = _config.Overlay.BackgroundColor ?? "";
        OverlayBorderEnabled = _config.Overlay.BorderEnabled;
        OverlayBorderColorText = _config.Overlay.BorderColor ?? "";
        OverlayFontSizeSlider = _config.Overlay.FontSize;
        OverlayCompactModeSetting = _config.Overlay.CompactMode;
        SetOverlayFontFamilyDraft(_config.Overlay.FontFamily);
        RaiseOverlayPreviewProperties();
    }

    private void SetOverlayFontFamilyDraft(string? configuredFontFamily)
    {
        var configuredFamily = configuredFontFamily?.Trim() ?? "";
        var installedFamily = OverlayFontFamilyOptions.FirstOrDefault(name =>
            !name.Equals("Padrão do app", StringComparison.Ordinal)
            && name.Equals(configuredFamily, StringComparison.OrdinalIgnoreCase));
        OverlayFontFamilySelection = installedFamily ?? "Padrão do app";
        OverlayFontFamilyCustomText = installedFamily is null ? configuredFamily : "";
    }
}
