using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

internal static class OverlayMetricsBuilder
{
    private const int MaxInstanceRows = 24;

    /// <summary>Plano declarativo de uma row por tick: identidade estável para a
    /// reconciliação + textos do tick. A instância do VM persiste; o plano só descreve.</summary>
    public readonly record struct OverlayRowPlan(
        string Identity, string Name, string Value, bool ShowFrametimeGraph,
        string? OverrideIdentity = null, SensorKey? SeriesKey = null, double? NumericValue = null,
        bool? ShowNameOverride = null, bool IsIndividualSeries = false);

    public static IReadOnlyList<OverlayRowPlan> Plan(
        SensorData? data,
        OverlayConfigSelection selection,
        IReadOnlyList<string>? shownSeries = null,
        bool includePrimarySeries = false)
    {
        var rows = new List<OverlayRowPlan>();

        // Chaves capturadas uma única vez por tick (antes: cada TryValue re-criava
        // a lista ordenada — ~15x por snapshot).
        var keys = data?.GetSeriesKeys();

        if (selection.ShowCpu)
        {
            var parts = new List<string>
            {
                TryValue(data, keys, SensorType.CpuTotal, out var usage) ? $"{usage:F0}%" : "—",
                TryValue(data, keys, SensorType.CpuTemperature, out var cpuTemp) ? $"{cpuTemp:F0} °C" : "—",
                TryValue(data, keys, SensorType.CpuClock, out var cpuClock) ? $"{cpuClock:F0} MHz" : "—"
            };
            rows.Add(new OverlayRowPlan("CPU", "CPU", string.Join(" · ", parts), false));
        }

        if (selection.ShowGpu)
        {
            var hasGpuMemoryUsed = TryValue(data, keys, SensorType.GpuMemoryUsed, out var gpuMemoryUsed);
            var hasGpuMemoryTotal = TryValue(data, keys, SensorType.GpuMemoryTotal, out var gpuMemoryTotal);
            var parts = new List<string>
            {
                TryValue(data, keys, SensorType.GpuCore, out var usage) ? $"{usage:F0}%" : "—",
                TryValue(data, keys, SensorType.GpuTemperature, out var gpuTemp) ? $"{gpuTemp:F0} °C" : "—",
                hasGpuMemoryUsed && hasGpuMemoryTotal
                    ? $"{gpuMemoryUsed / 1024:F1}/{gpuMemoryTotal / 1024:F1} GB"
                    : "—"
            };
            rows.Add(new OverlayRowPlan("GPU", "GPU", string.Join(" · ", parts), false));
        }

        if (selection.ShowRam)
        {
            var hasRamUsed = TryValue(data, keys, SensorType.RamUsed, out var ramUsed);
            var hasRamTotal = TryValue(data, keys, SensorType.RamTotal, out var ramTotal);
            var parts = new List<string>
            {
                TryValue(data, keys, SensorType.RamUsage, out var usage) ? $"{usage:F0}%" : "—",
                hasRamUsed && hasRamTotal
                    ? $"{ramUsed:F1}/{ramTotal:F1} GB"
                    : "—"
            };
            rows.Add(new OverlayRowPlan("RAM", "RAM", string.Join(" · ", parts), false));
        }

        if (selection.ShowDisk)
        {
            var read = TryValue(data, keys, SensorType.DiskRead, out var readValue) ? RateText(readValue) : "—";
            var write = TryValue(data, keys, SensorType.DiskWrite, out var writeValue) ? RateText(writeValue) : "—";
            rows.Add(new OverlayRowPlan("DISCO", "DISCO", $"↓ {read} · ↑ {write}", false));
        }

        if (selection.ShowNetwork)
        {
            var down = TryValue(data, keys, SensorType.NetworkDown, out var downValue) ? RateText(downValue) : "—";
            var up = TryValue(data, keys, SensorType.NetworkUp, out var upValue) ? RateText(upValue) : "—";
            rows.Add(new OverlayRowPlan("REDE", "REDE", $"↓ {down} · ↑ {up}", false));
        }

        if (selection.ShowFans)
        {
            var fans = keys?
                .Where(key => key.Type is SensorType.FanSpeed or SensorType.GpuFan)
                .Take(4)
                .ToArray() ?? [];
            if (fans.Length == 0)
                rows.Add(new OverlayRowPlan("VENTOINHA", "VENTOINHA", "—", false));
            else
                foreach (var fan in fans)
                {
                    var fanName = fan.Type == SensorType.GpuFan ? "GPU FAN" : fan.Index > 0 ? $"FAN {fan.Index}" : "FAN";
                    rows.Add(new OverlayRowPlan(
                        fan.ToString(),
                        fanName,
                        fan.Type == SensorType.GpuFan ? $"{data!.GetValue(fan):F0}%" : $"{data!.GetValue(fan):F0} RPM",
                        false,
                        fan.ToString(), fan, data!.GetValue(fan), false));
                }
        }

        if (selection.ShowMotherboard)
        {
            var temperatures = keys?
                .Where(key => key.Type is SensorType.MotherboardTemp or SensorType.VrmTemperature)
                .Select(key => data!.GetValue(key))
                .ToArray() ?? [];
            rows.Add(new OverlayRowPlan("PLACA", "PLACA", temperatures.Length > 0
                ? $"{temperatures.Max():F0} °C"
                : "—", false));
        }

        if (selection.ShowFps)
        {
            var fps = TryValue(data, keys, SensorType.Fps, out var fpsValue) ? $"{fpsValue:F0} FPS" : "— FPS";
            var frametime = TryValue(data, keys, SensorType.FrametimeMs, out var frameValue) ? $"{frameValue:F2} ms" : "— ms";
            rows.Add(new OverlayRowPlan(
                "JOGO",
                "JOGO",
                $"{fps} · {frametime}",
                selection.ShowFrametimeGraph && data is not null && data.FrametimeHistory.Length > 1));
        }

        if (data is not null && shownSeries is { Count: > 0 })
        {
            var instanceRows = data.Series.Keys
                .Where(key => (includePrimarySeries || !key.IsPrimary) && shownSeries.Any(key.MatchesPattern))
                .Distinct()
                .OrderBy(FamilyOrder)
                .ThenBy(key => key.Type.ToString(), StringComparer.Ordinal)
                .ThenBy(key => key.Index)
                .Take(MaxInstanceRows);

            foreach (var key in instanceRows)
            {
                var formatted = SensorFormatter.FormatSeries(key, data.Series[key], showZero: true);
                var label = data.GetLabel(key);
                var description = string.Equals(label, key.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? formatted
                    : $"{formatted} · {label}";
                var identity = key.ToString();
                rows.Add(new OverlayRowPlan(identity, identity, description, false, identity, key, data.Series[key],
                    IsIndividualSeries: true));
            }
        }

        return rows;
    }

    private static int FamilyOrder(SensorKey key) => key.Type switch
    {
        SensorType.GpuCore or SensorType.GpuTemperature or SensorType.GpuMemory or SensorType.GpuMemoryUsed or
            SensorType.GpuMemoryTotal or SensorType.GpuFan or SensorType.GpuPower or SensorType.GpuCoreClock or
            SensorType.GpuMemoryClock or SensorType.GpuVoltage or SensorType.GpuHotSpot or SensorType.GpuVramTemperature => 0,
        SensorType.FanSpeed => 1,
        SensorType.DiskRead or SensorType.DiskWrite or SensorType.DiskTotalRead or SensorType.DiskTotalWritten or
            SensorType.DiskUsage or SensorType.DiskHealth or SensorType.DiskTemperature => 2,
        SensorType.RamTemperature => 3,
        SensorType.CpuTotal or SensorType.CpuPerCore or SensorType.CpuTemperature or SensorType.CpuClock or
            SensorType.CpuPower or SensorType.CpuVoltage => 4,
        SensorType.NetworkUp or SensorType.NetworkDown or SensorType.NetworkUpTotal or SensorType.NetworkDownTotal => 5,
        SensorType.BatteryLevel or SensorType.BatteryWearLevel or SensorType.BatteryVoltage or SensorType.BatteryPower => 6,
        _ => 7
    };

    private static bool TryValue(SensorData? data, IReadOnlyList<SensorKey>? keys, SensorType type, out double value)
    {
        if (data is not null && keys is not null)
        {
            SensorKey key = default;
            var found = false;
            for (var i = 0; i < keys.Count; i++)
            {
                if (keys[i].Type != type) continue;
                key = keys[i];
                found = true;
                break;
            }
            if (found && (key.IsPrimary ? data.Values.ContainsKey(type) : data.Series.ContainsKey(key)))
            {
                value = data.GetValue(key);
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>Formatação de taxa (MB/s, KB/s) compartilhada: as linhas do overlay e
    /// os cards do painel usavam duas cópias idênticas desta função.</summary>
    internal static string RateText(double bytesPerSecond) => bytesPerSecond >= 1024 * 1024
        ? $"{bytesPerSecond / (1024 * 1024):F1} MB/s"
        : $"{bytesPerSecond / 1024:F0} KB/s";
}
internal readonly record struct OverlayConfigSelection(
    bool ShowCpu,
    bool ShowGpu,
    bool ShowRam,
    bool ShowDisk,
    bool ShowNetwork,
    bool ShowFans,
    bool ShowMotherboard,
    bool ShowFps,
    bool ShowFrametimeGraph);
