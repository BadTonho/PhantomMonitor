using System.Text.Json.Serialization;

namespace RivaMonitor.Models;

public sealed class SensorData
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<SensorType, double> Values { get; set; } = [];
    public Dictionary<SensorType, string> Labels { get; set; } = [];

    [JsonIgnore]
    public List<GpuProcessInfo> GpuProcesses { get; set; } = [];

    public double GetValue(SensorType type) =>
        Values.TryGetValue(type, out var v) ? v : 0;

    public string GetLabel(SensorType type) =>
        Labels.TryGetValue(type, out var l) ? l : type.ToString();

    public double CpuUsage => GetValue(SensorType.CpuTotal);
    public double CpuTemp => GetValue(SensorType.CpuTemperature);
    public double GpuUsage => GetValue(SensorType.GpuCore);
    public double GpuTemp => GetValue(SensorType.GpuTemperature);
    public double RamUsagePercent => GetValue(SensorType.RamUsage);
    public double RamAvailableGB => GetValue(SensorType.RamAvailable);
}

public sealed class GpuProcessInfo
{
    public string Name { get; set; } = "";
    public double Usage { get; set; }
    public ulong MemoryMb { get; set; }
}
