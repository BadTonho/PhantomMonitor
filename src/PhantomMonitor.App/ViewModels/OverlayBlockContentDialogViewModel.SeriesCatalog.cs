using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class OverlayBlockContentDialogViewModel
{
    private const string AllSeriesCategories = "Todas";

    private static readonly (string Key, string Name)[] Categories =
    [
        ("cpu", "CPU"), ("gpu", "GPU"), ("ram", "RAM"), ("disk", "Disco"),
        ("network", "Rede"), ("fans", "Ventoinhas"), ("motherboard", "Placa-mãe"), ("fps", "FPS / frametime")
    ];

    public IReadOnlyList<string> SeriesCategories { get; } =
    [
        AllSeriesCategories, "CPU", "GPU", "RAM", "Disco", "Rede", "Ventoinhas",
        "Placa-mãe", "Bateria", "FPS", "Outros"
    ];

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

    public void AddSeriesPattern()
    {
        var value = SeriesPatternInput.Trim();
        if (!IsValidPattern(value) || _selected.Contains(("series", value))) return;
        SetSelected("series", value, true);
        SeriesPatternInput = "";
        RefreshDynamicChoices();
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
            SeriesChoices.Add(CreateChoice("series", value, SeriesChipLabel(value), selectionChanged: _ => Raise(nameof(SeriesCategorySelectionState))));
        Raise(nameof(HasSeriesChoices));
        Raise(nameof(NoSeriesChoices));
        Raise(nameof(SeriesCategorySelectionState));
    }

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
            or SensorType.RamCommitted or SensorType.RamCommitLimit or SensorType.RamFrequency
            or SensorType.RamCasLatency or SensorType.RamTrcd or SensorType.RamTrp or SensorType.RamTras
            or SensorType.RamSystemUsed or SensorType.RamGameUsed
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

    private static string SeriesChipLabel(string value)
    {
        if (!SensorKey.TryParse(value, out var key) || !key.IsPrimary) return value;
        return key.Type switch
        {
            SensorType.RamUsage => "RamUsage · Uso (%)",
            SensorType.RamUsed => "RamUsed · Memória usada",
            SensorType.RamAvailable => "RamAvailable · Memória disponível",
            SensorType.RamTotal => "RamTotal · Memória total",
            SensorType.RamCommitted => "RamCommitted · Memória comprometida",
            SensorType.RamCommitLimit => "RamCommitLimit · Limite comprometido",
            SensorType.RamFrequency => "RamFrequency · Frequência",
            SensorType.RamCasLatency => "RamCasLatency · Timings (CL)",
            SensorType.RamTrcd => "RamTrcd · Timing tRCD",
            SensorType.RamTrp => "RamTrp · Timing tRP",
            SensorType.RamTras => "RamTras · Timing tRAS",
            SensorType.RamSystemUsed => "RamSystemUsed · Memória do sistema",
            SensorType.RamGameUsed => "RamGameUsed · Memória do jogo",
            _ => value
        };
    }

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

    private static bool IsValidPattern(string value)
    {
        if (value == "*") return true;
        if (value.Count(character => character == '*') == 0)
            return SensorKey.TryParse(value, out var key) && Enum.IsDefined(key.Type);
        return value.Count(character => character == '*') == 1
            && (value.EndsWith('*') || value.StartsWith('*')) && value.Length > 1;
    }
}

