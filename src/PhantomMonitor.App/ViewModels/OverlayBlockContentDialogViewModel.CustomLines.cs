using System.ComponentModel;
using System.Globalization;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class OverlayBlockContentDialogViewModel
{
    private bool _loadingCustomLine;
    private string _customLineText = "";
    private string _customLineFontSize = "";
    private string _customLineWidth = "";
    private string? _hardwareCategory;
    private readonly OverlayHardwareSelection _hardwareSelection = new();
    private IReadOnlyList<OverlayHardwareOption> _hardwareOptions = [];
    private string _hardwareSelectionMessage = "";
    private string _customLineMessage = "";

    public bool HasSelectedCustomLine => SelectedOrderRow is { IsMetric: false, Group.Kind: "customline" };
    public IReadOnlyList<string> HardwareCategories { get; } = ["GPU", "CPU", "Placa-mãe"];
    public IReadOnlyList<OverlayHardwareOption> HardwareOptions { get => _hardwareOptions; private set => Set(ref _hardwareOptions, value); }
    public bool ShowHardwareOptions => HardwareOptions.Count > 1;
    public bool IsSyncingHardwareSelection { get; private set; }
    public string HardwareSelectionMessage { get => _hardwareSelectionMessage; private set => Set(ref _hardwareSelectionMessage, value); }
    public string CustomLineMessage
    {
        get => _customLineMessage;
        private set { if (Set(ref _customLineMessage, value)) Raise(nameof(CanApplyCustomLine)); }
    }
    public bool CanApplyCustomLine => CustomLineMessage.Length == 0;
    public string? HardwareCategory
    {
        get => _hardwareCategory;
        set => Set(ref _hardwareCategory, value);
    }
    public string CustomLineText
    {
        get => _customLineText;
        set { if (Set(ref _customLineText, value ?? "")) UpdateCustomLine(); }
    }
    public string CustomLineFontSize
    {
        get => _customLineFontSize;
        set { if (Set(ref _customLineFontSize, value ?? "")) UpdateCustomLine(); }
    }
    public string CustomLineWidth
    {
        get => _customLineWidth;
        set { if (Set(ref _customLineWidth, value ?? "")) UpdateCustomLine(); }
    }

    public void AddCustomLine()
    {
        var line = new OverlayCustomLineConfig();
        _customLines.Add(line);
        SetSelected("customline", line.Id, true, publishDraft: false);
    }

    public HardwareIdentity? ChooseHardwareCategory(string category)
    {
        var device = _hardwareSelection.ChooseCategory(category, _owner.OverlayHardwareDevices);
        SyncHardwareSelection();
        return device;
    }

    public HardwareIdentity? ChooseHardwareDevice(OverlayHardwareOption option)
    {
        var device = _hardwareSelection.ChooseDevice(option);
        SyncHardwareSelection();
        return device;
    }

    public string? CreateHardwareToken(HardwareIdentity device)
    {
        var line = CurrentCustomLine();
        if (line is null) return null;
        var existing = line.Hardware.FirstOrDefault(reference => reference.DeviceId == device.Id && reference.Category == device.Category);
        if (existing is null)
        {
            var category = device.Category switch { "cpu" => "CPU", "gpu" => "GPU", _ => "Placa-mãe" };
            var index = 1;
            while (line.Hardware.Any(reference => reference.Token == $"{category} {index}")) index++;
            existing = new OverlayHardwareReference { Token = $"{category} {index}", DeviceId = device.Id, Category = device.Category };
            line.Hardware.Add(existing);
        }
        return "{" + existing.Token + "}";
    }

    private OverlayCustomLineConfig? CurrentCustomLine() => HasSelectedCustomLine
        ? _customLines.FirstOrDefault(line => line.Id == SelectedOrderRow!.Group.Value) : null;

    private void LoadCustomLineEditor()
    {
        _loadingCustomLine = true;
        var line = CurrentCustomLine();
        CustomLineText = line?.Text ?? "";
        CustomLineFontSize = line?.FontSize?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        CustomLineWidth = line?.Width?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        CustomLineMessage = "";
        _loadingCustomLine = false;
        _hardwareSelection.Reset();
        SyncHardwareSelection();
    }

    private void UpdateCustomLine()
    {
        if (_loadingCustomLine || CurrentCustomLine() is not { } line) return;
        line.Text = CustomLineText;
        var fontValid = TryOptionalSize(CustomLineFontSize, 8, 20, out var fontSize);
        var widthValid = TryOptionalSize(CustomLineWidth, 40, 700, out var width);
        if (fontValid) line.FontSize = fontSize;
        if (widthValid) line.Width = width;
        CustomLineMessage = fontValid && widthValid ? "" : "Fonte: 8–20 pt; largura: 40–700 px. Deixe vazio para usar o padrão.";
        SelectedOrderRow!.Group.UpdateLabel(CustomLineLabel(line.Id));
        PublishDraftToCanvas();
    }

    private static bool TryOptionalSize(string text, double min, double max, out double? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            || !double.IsFinite(parsed) || parsed < min || parsed > max) return false;
        result = parsed;
        return true;
    }

    private string CustomLineLabel(string id)
    {
        var line = _customLines.FirstOrDefault(item => item.Id == id);
        var label = line is null ? "" : OverlayCustomLineRenderer.Render(line, _owner.OverlayHardwareDevices);
        return string.IsNullOrWhiteSpace(label) ? "Linha vazia" : label;
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.OverlayHardwareDevices)) return;
        _hardwareSelection.Refresh(_owner.OverlayHardwareDevices);
        SyncHardwareSelection();
        foreach (var item in OrderedContentItems.Where(item => item.Kind == "customline"))
            item.UpdateLabel(CustomLineLabel(item.Value));
    }

    private void SyncHardwareSelection()
    {
        IsSyncingHardwareSelection = true;
        try
        {
            HardwareOptions = _hardwareSelection.Options;
            HardwareCategory = _hardwareSelection.Category;
            HardwareSelectionMessage = _hardwareSelection.Message;
            Raise(nameof(ShowHardwareOptions));
        }
        finally { IsSyncingHardwareSelection = false; }
    }
}
