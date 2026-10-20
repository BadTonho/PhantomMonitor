using System.Globalization;
using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

internal static class SensorInventoryBuilder
{
    private sealed record GroupDefinition(string Title, SensorType[] ExpectedTypes);

    private static readonly GroupDefinition[] Groups =
    [
        new("CPU", [SensorType.CpuTotal, SensorType.CpuPerCore, SensorType.CpuTemperature, SensorType.CpuClock]),
        new("GPU", [SensorType.GpuCore, SensorType.GpuMemoryUsed, SensorType.GpuMemoryTotal,
            SensorType.GpuTemperature, SensorType.GpuHotSpot, SensorType.GpuVramTemperature]),
        new("Memória", [SensorType.RamUsage, SensorType.RamUsed, SensorType.RamTotal, SensorType.RamAvailable]),
        new("Disco", [SensorType.DiskRead, SensorType.DiskWrite, SensorType.DiskUsage]),
        new("Rede", [SensorType.NetworkDown, SensorType.NetworkUp,
            SensorType.NetworkDownTotal, SensorType.NetworkUpTotal]),
        new("Ventoinhas", [SensorType.FanSpeed, SensorType.GpuFan]),
        new("Bateria", [SensorType.BatteryLevel, SensorType.BatteryVoltage, SensorType.BatteryPower,
            SensorType.BatteryWearLevel, SensorType.BatteryAcPower]),
        new("FPS", [SensorType.Fps, SensorType.FrametimeMs, SensorType.FpsAvg, SensorType.FpsMin,
            SensorType.Fps1Low, SensorType.Fps01Low, SensorType.FpsMax])
    ];

    public static IReadOnlyList<InventoryGroupViewModel> Build(SensorData data)
    {
        var keys = data.GetSeriesKeys();
        var knownTypes = Groups.SelectMany(group => group.ExpectedTypes).ToHashSet();
        var result = new List<InventoryGroupViewModel>(Groups.Length + 1);

        foreach (var group in Groups)
        {
            var groupTypes = group.ExpectedTypes.ToHashSet();
            var rows = keys
                .Where(key => groupTypes.Contains(key.Type))
                .OrderBy(key => key.Type)
                .ThenBy(key => key.Index)
                .Select(key => AvailableRow(data, key))
                .ToList();

            foreach (var type in group.ExpectedTypes)
            {
                if (keys.Any(key => key.Type == type)) continue;
                rows.Add(new InventoryRowViewModel(
                    new SensorKey(type, 0).ToString(),
                    "—",
                    "Indisponível",
                    SensorUnits.Of(type),
                    false));
            }

            result.Add(new InventoryGroupViewModel(group.Title, rows));
        }

        var otherRows = keys
            .Where(key => !knownTypes.Contains(key.Type))
            .OrderBy(key => key.Type)
            .ThenBy(key => key.Index)
            .Select(key => AvailableRow(data, key))
            .ToList();
        if (otherRows.Count > 0)
            result.Add(new InventoryGroupViewModel("Outros", otherRows));

        return result;
    }

    private static InventoryRowViewModel AvailableRow(SensorData data, SensorKey key)
    {
        var value = data.GetValue(key);
        var (valueText, unit) = Format(key.Type, value);
        return new InventoryRowViewModel(
            key.ToString(),
            data.GetLabel(key),
            valueText,
            unit,
            true);
    }

    private static (string Value, string Unit) Format(SensorType type, double value)
    {
        if (type == SensorType.BatteryAcPower)
            return (value == 0 ? "Desconectada" : "Conectada", "Alimentação AC");

        if (type is SensorType.DiskRead or SensorType.DiskWrite or SensorType.NetworkDown or SensorType.NetworkUp)
        {
            var abs = Math.Abs(value);
            return abs switch
            {
                >= 1024 * 1024 => ($"{value / (1024 * 1024):F1}", "MB/s"),
                >= 1024 => ($"{value / 1024:F1}", "KB/s"),
                _ => ($"{value:F0}", "B/s")
            };
        }

        if (type is SensorType.NetworkDownTotal or SensorType.NetworkUpTotal)
            return FormatByteCount(value);

        if (type is SensorType.CpuClock or SensorType.GpuCoreClock or SensorType.GpuMemoryClock)
            return value >= 1000 ? ($"{value / 1000:F2}", "GHz") : ($"{value:F0}", "MHz");

        return (value.ToString("F2", CultureInfo.CurrentCulture), SensorUnits.Of(type));
    }

    private static (string Value, string Unit) FormatByteCount(double bytes)
    {
        if (Math.Abs(bytes) >= 1024d * 1024 * 1024)
            return ($"{bytes / (1024d * 1024 * 1024):F2}", "GB");
        if (Math.Abs(bytes) >= 1024d * 1024)
            return ($"{bytes / (1024d * 1024):F1}", "MB");
        if (Math.Abs(bytes) >= 1024)
            return ($"{bytes / 1024:F1}", "KB");
        return ($"{bytes:F0}", "B");
    }
}
