using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

public sealed record DiagnosticSensorWarningViewModel(
    string Device,
    string Metric,
    string Source,
    string Reason)
{
    public string SourceLabel => $"Fonte: {Source}";
    public bool IsGeneralWarning => Metric == "Detecção de GPU";

    public static DiagnosticSensorWarningViewModel From(SensorAvailabilityDiagnostic diagnostic) => new(
        diagnostic.Device,
        diagnostic.Metric is { } metric ? MetricName(metric) : "Detecção de GPU",
        diagnostic.Source,
        diagnostic.Reason);

    private static string MetricName(SensorType metric) => metric switch
    {
        SensorType.GpuCore => "Uso da GPU",
        SensorType.GpuMemoryUsed => "VRAM usada",
        SensorType.GpuMemoryTotal => "VRAM total",
        SensorType.GpuTemperature => "Temperatura",
        SensorType.GpuHotSpot => "Hotspot",
        SensorType.GpuVramTemperature => "Temperatura da VRAM",
        _ => metric.ToString()
    };
}
