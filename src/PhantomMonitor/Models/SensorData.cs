using System.Text.Json.Serialization;

namespace PhantomMonitor.Models;

public sealed class SensorData
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<SensorType, double> Values { get; set; } = [];
    public Dictionary<SensorType, string> Labels { get; set; } = [];

    [JsonIgnore]
    public List<GpuProcessInfo> GpuProcesses { get; set; } = [];
    [JsonIgnore]
    public List<string> SensorDebugInfo { get; set; } = [];

    public double GetValue(SensorType type) =>
        Values.TryGetValue(type, out var v) ? v : 0;

    public void SetValue(SensorType type, double value, string? label = null)
    {
        Values[type] = value;
        if (!string.IsNullOrEmpty(label))
        {
            Labels[type] = label;
        }
    }

    public double CpuUsage => GetValue(SensorType.CpuTotal);
    public double CpuTemp => GetValue(SensorType.CpuTemperature);
    public double GpuUsage => GetValue(SensorType.GpuCore);
    public double GpuTemp => GetValue(SensorType.GpuTemperature);
    public double RamUsagePercent => GetValue(SensorType.RamUsage);
    public double RamAvailableGB => GetValue(SensorType.RamAvailable);

    // FPS
    public double Fps => GetValue(SensorType.Fps);
    public double FrametimeMs => GetValue(SensorType.FrametimeMs);
    public double FpsAvg => GetValue(SensorType.FpsAvg);
    public double FpsMin => GetValue(SensorType.FpsMin);
    public double Fps1Low => GetValue(SensorType.Fps1Low);
    public double Fps01Low => GetValue(SensorType.Fps01Low);
    public double FpsMax => GetValue(SensorType.FpsMax);
    [JsonIgnore]
    public float[] FrametimeHistory { get; set; } = [];
}

public sealed class GpuProcessInfo
{
    public string Name { get; set; } = "";
    public double Usage { get; set; }
    public ulong MemoryMb { get; set; }
}
