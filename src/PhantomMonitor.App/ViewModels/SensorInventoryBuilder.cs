using System.Collections.ObjectModel;
using System.Globalization;
using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

/// <summary>Plano declarativo de uma linha. Famílias com instâncias guardam seus detalhes em Children.</summary>
internal sealed record InventoryRowPlan(string Key, string Source, string Value, string Unit, bool Available,
    string Min = "--", string Max = "--", string Avg = "--", IReadOnlyList<InventoryRowPlan>? Children = null);

internal sealed record InventoryGroupPlan(string Title, IReadOnlyList<InventoryRowPlan> Rows);

/// <summary>Estatísticas em memória das médias calculadas para famílias sem série agregada publicada.</summary>
internal sealed class InventoryAggregateStatsTracker
{
    private sealed class Accumulator(double value)
    {
        public double Min { get; private set; } = value;
        public double Max { get; private set; } = value;
        public double Total { get; private set; } = value;
        public long Count { get; private set; } = 1;

        public void Add(double next)
        {
            Min = Math.Min(Min, next);
            Max = Math.Max(Max, next);
            Total += next;
            Count++;
        }

        public (double Min, double Max, double Average) Snapshot() => (Min, Max, Total / Count);
    }

    private readonly Dictionary<string, Accumulator> _values = new(StringComparer.Ordinal);

    public (double Min, double Max, double Average) Observe(string identity, double value)
    {
        if (!_values.TryGetValue(identity, out var accumulator))
            _values[identity] = accumulator = new Accumulator(value);
        else
            accumulator.Add(value);
        return accumulator.Snapshot();
    }

    public void Reset() => _values.Clear();
}

/// <summary>Gera os grupos e famílias do inventário a partir de um snapshot.</summary>
internal static class SensorInventoryBuilder
{
    private sealed record GroupDefinition(string Title, SensorType[] ExpectedTypes);

    private static readonly GroupDefinition[] Groups =
    [
        new("CPU", [SensorType.CpuTotal, SensorType.CpuPerCore, SensorType.CpuTemperature, SensorType.CpuClock]),
        new("GPU", [SensorType.GpuCore, SensorType.GpuMemory, SensorType.GpuMemoryUsed, SensorType.GpuMemoryTotal,
            SensorType.GpuTemperature, SensorType.GpuHotSpot, SensorType.GpuVramTemperature,
            SensorType.GpuCoreClock, SensorType.GpuMemoryClock, SensorType.GpuVoltage, SensorType.GpuFan,
            SensorType.GpuPower, SensorType.GpuBusy]),
        new("Memória", [SensorType.RamUsage, SensorType.RamUsed, SensorType.RamTotal, SensorType.RamAvailable]),
        new("Disco", [SensorType.DiskRead, SensorType.DiskWrite, SensorType.DiskUsage]),
        new("Rede", [SensorType.NetworkDown, SensorType.NetworkUp,
            SensorType.NetworkDownTotal, SensorType.NetworkUpTotal]),
        new("Ventoinhas", [SensorType.FanSpeed]),
        new("Bateria", [SensorType.BatteryLevel, SensorType.BatteryVoltage, SensorType.BatteryPower,
            SensorType.BatteryWearLevel, SensorType.BatteryAcPower]),
        new("FPS", [SensorType.Fps, SensorType.FrametimeMs, SensorType.FpsAvg, SensorType.FpsMin,
            SensorType.Fps1Low, SensorType.Fps01Low, SensorType.FpsMax])
    ];

    public static IReadOnlyList<InventoryGroupViewModel> CreateShellGroups()
    {
        var groups = new InventoryGroupViewModel[Groups.Length];
        for (var index = 0; index < Groups.Length; index++)
            groups[index] = new InventoryGroupViewModel(Groups[index].Title, new ObservableCollection<InventoryRowViewModel>());
        return groups;
    }

    public static InventoryGroupViewModel CreateOtherGroup() =>
        new("Outros", new ObservableCollection<InventoryRowViewModel>());

    public static IReadOnlyList<InventoryGroupPlan> Plan(SensorData data, InventoryAggregateStatsTracker? aggregateStats = null,
        bool useSnapshotStats = true)
    {
        var keys = data.GetSeriesKeys();
        var knownTypes = Groups.SelectMany(group => group.ExpectedTypes).ToHashSet();
        var result = new List<InventoryGroupPlan>(Groups.Length + 1);

        foreach (var group in Groups)
        {
            var rows = new List<InventoryRowPlan>();
            var cpuTotalAdded = false;
            var coreInstances = group.Title == "CPU"
                ? keys.Where(key => key.Type == SensorType.CpuPerCore && key.Index > 0).OrderBy(key => key.Index).ToArray()
                : [];

            foreach (var type in group.ExpectedTypes)
            {
                if (group.Title == "CPU" && type is SensorType.CpuTotal or SensorType.CpuPerCore)
                {
                    if (!cpuTotalAdded && type == SensorType.CpuTotal)
                    {
                        var totalKey = new SensorKey(SensorType.CpuTotal, 0);
                        var hasTotal = keys.Contains(totalKey);
                        if (coreInstances.Length > 0)
                        {
                            var children = coreInstances.Select(key => AvailableRow(data, key, useSnapshotStats)).ToArray();
                            rows.Add(hasTotal
                                ? AvailableRow(data, totalKey, useSnapshotStats, children)
                                : DerivedAverageRow(data, SensorType.CpuTotal, coreInstances, children, aggregateStats, useSnapshotStats));
                        }
                        else if (hasTotal)
                        {
                            rows.Add(FamilyRow(data, SensorType.CpuTotal, keys, useSnapshotStats, aggregateStats));
                        }
                        else if (keys.Any(key => key.Type == SensorType.CpuPerCore))
                        {
                            rows.Add(FamilyRow(data, SensorType.CpuPerCore, keys, useSnapshotStats, aggregateStats));
                        }
                        else
                        {
                            rows.Add(UnavailableRow(SensorType.CpuTotal));
                        }
                        cpuTotalAdded = true;
                    }

                    // CPU Total substitui a linha canônica CpuPerCore (o máximo duplicava os núcleos).
                    if (type == SensorType.CpuPerCore)
                    {
                        if (coreInstances.Length == 0 && keys.Any(key => key.Type == SensorType.CpuPerCore))
                            continue; // Família sem CpuTotal: exibida no slot de CPU Total acima.
                        if (coreInstances.Length > 0) continue;
                        if (!keys.Any(key => key.Type == SensorType.CpuPerCore))
                            rows.Add(UnavailableRow(SensorType.CpuPerCore));
                    }
                    continue;
                }

                var typeKeys = keys.Where(key => key.Type == type).OrderBy(key => key.Index).ToArray();
                if (typeKeys.Length == 0)
                    rows.Add(UnavailableRow(type));
                else
                    rows.Add(FamilyRow(data, type, keys, useSnapshotStats, aggregateStats));
            }

            result.Add(new InventoryGroupPlan(group.Title, rows));
        }

        var otherRows = keys
            .Where(key => !knownTypes.Contains(key.Type))
            .OrderBy(key => key.Type)
            .ThenBy(key => key.Index)
            .GroupBy(key => key.Type)
            .SelectMany(family => family.Key == SensorType.Custom
                ? family.Select(key => AvailableRow(data, key, useSnapshotStats))
                : [FamilyRow(data, family.Key, keys, useSnapshotStats, aggregateStats)])
            .ToArray();
        if (otherRows.Length > 0)
            result.Add(new InventoryGroupPlan("Outros", otherRows));

        return result;
    }

    private static InventoryRowPlan FamilyRow(SensorData data, SensorType type, IReadOnlyList<SensorKey> allKeys,
        bool useSnapshotStats, InventoryAggregateStatsTracker? aggregateStats)
    {
        var typeKeys = allKeys.Where(key => key.Type == type).OrderBy(key => key.Index).ToArray();
        var primary = typeKeys.FirstOrDefault(key => key.IsPrimary);
        var hasPrimary = typeKeys.Any(key => key.IsPrimary);
        var childrenKeys = typeKeys.Where(key => !key.IsPrimary).ToArray();

        if (childrenKeys.Length == 0)
            return AvailableRow(data, typeKeys[0], useSnapshotStats);

        var children = childrenKeys.Select(key => AvailableRow(data, key, useSnapshotStats)).ToArray();
        return hasPrimary
            ? AvailableRow(data, primary, useSnapshotStats, children)
            : DerivedAverageRow(data, type, childrenKeys, children, aggregateStats, useSnapshotStats);
    }

    private static InventoryRowPlan DerivedAverageRow(SensorData data, SensorType summaryType,
        IReadOnlyList<SensorKey> instanceKeys, IReadOnlyList<InventoryRowPlan> children,
        InventoryAggregateStatsTracker? aggregateStats, bool useSnapshotStats)
    {
        var values = instanceKeys.Select(data.GetValue).Where(double.IsFinite).ToArray();
        if (values.Length == 0)
            return new InventoryRowPlan($"{summaryType}:average", $"Média de {summaryType}", "Indisponível",
                SensorUnits.Of(summaryType), false, Children: children);

        var average = values.Average();
        var (valueText, unit) = Format(summaryType, average);
        var stats = useSnapshotStats ? aggregateStats?.Observe($"{summaryType}:average", average) : null;
        var summaryKey = new SensorKey(summaryType, 0);
        return new InventoryRowPlan(
            $"{summaryType}:average",
            $"Média · {data.GetLabel(instanceKeys[0])}",
            valueText,
            unit,
            true,
            stats is { } tracked ? Stat(tracked.Min, summaryKey) : "--",
            stats is { } trackedMax ? Stat(trackedMax.Max, summaryKey) : "--",
            stats is { } trackedAvg ? Stat(trackedAvg.Average, summaryKey) : "--",
            children);
    }

    private static InventoryRowPlan UnavailableRow(SensorType type) =>
        new(new SensorKey(type, 0).ToString(), "—", "Indisponível", SensorUnits.Of(type), false);

    private static InventoryRowPlan AvailableRow(SensorData data, SensorKey key, bool useSnapshotStats,
        IReadOnlyList<InventoryRowPlan>? children = null)
    {
        var value = data.GetValue(key);
        var (valueText, unit) = Format(key.Type, value);
        data.SeriesStats.TryGetValue(key, out var stats);

        return new InventoryRowPlan(
            key.ToString(),
            data.GetLabel(key),
            valueText,
            unit,
            true,
            Stat(useSnapshotStats ? stats?.Min : null, key),
            Stat(useSnapshotStats ? stats?.Max : null, key),
            Stat(useSnapshotStats ? stats?.Average : null, key),
            children);
    }

    private static string Stat(double? stat, SensorKey key)
    {
        if (key.Type == SensorType.BatteryAcPower) return "--";
        if (stat is not { } value || !double.IsFinite(value)) return "--";
        var (text, unit) = Format(key.Type, value);
        return unit.Length > 0 ? $"{text} {unit}" : text;
    }

    private static (string Value, string Unit) Format(SensorType type, double value)
    {
        if (!double.IsFinite(value)) return ("Indisponível", SensorUnits.Of(type));

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
