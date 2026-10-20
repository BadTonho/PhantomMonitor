using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

internal static class OverlayMetricsBuilder
{
    public static IReadOnlyList<OverlayMetricViewModel> Build(SensorData? data, OverlayConfigSelection selection)
    {
        var rows = new List<OverlayMetricViewModel>();

        if (selection.ShowCpu)
        {
            var parts = new List<string>
            {
                TryValue(data, SensorType.CpuTotal, out var usage) ? $"{usage:F0}%" : "—",
                TryValue(data, SensorType.CpuTemperature, out var cpuTemp) ? $"{cpuTemp:F0} °C" : "—",
                TryValue(data, SensorType.CpuClock, out var cpuClock) ? $"{cpuClock:F0} MHz" : "—"
            };
            rows.Add(new OverlayMetricViewModel("CPU", string.Join(" · ", parts)));
        }

        if (selection.ShowGpu)
        {
            var hasGpuMemoryUsed = TryValue(data, SensorType.GpuMemoryUsed, out var gpuMemoryUsed);
            var hasGpuMemoryTotal = TryValue(data, SensorType.GpuMemoryTotal, out var gpuMemoryTotal);
            var parts = new List<string>
            {
                TryValue(data, SensorType.GpuCore, out var usage) ? $"{usage:F0}%" : "—",
                TryValue(data, SensorType.GpuTemperature, out var gpuTemp) ? $"{gpuTemp:F0} °C" : "—",
                hasGpuMemoryUsed && hasGpuMemoryTotal
                    ? $"{gpuMemoryUsed / 1024:F1}/{gpuMemoryTotal / 1024:F1} GB"
                    : "—"
            };
            rows.Add(new OverlayMetricViewModel("GPU", string.Join(" · ", parts)));
        }

        if (selection.ShowRam)
        {
            var hasRamUsed = TryValue(data, SensorType.RamUsed, out var ramUsed);
            var hasRamTotal = TryValue(data, SensorType.RamTotal, out var ramTotal);
            var parts = new List<string>
            {
                TryValue(data, SensorType.RamUsage, out var usage) ? $"{usage:F0}%" : "—",
                hasRamUsed && hasRamTotal
                    ? $"{ramUsed:F1}/{ramTotal:F1} GB"
                    : "—"
            };
            rows.Add(new OverlayMetricViewModel("RAM", string.Join(" · ", parts)));
        }

        if (selection.ShowDisk)
        {
            var read = TryValue(data, SensorType.DiskRead, out var readValue) ? RateValue(readValue) : "—";
            var write = TryValue(data, SensorType.DiskWrite, out var writeValue) ? RateValue(writeValue) : "—";
            rows.Add(new OverlayMetricViewModel("DISCO", $"↓ {read} · ↑ {write}"));
        }

        if (selection.ShowNetwork)
        {
            var down = TryValue(data, SensorType.NetworkDown, out var downValue) ? RateValue(downValue) : "—";
            var up = TryValue(data, SensorType.NetworkUp, out var upValue) ? RateValue(upValue) : "—";
            rows.Add(new OverlayMetricViewModel("REDE", $"↓ {down} · ↑ {up}"));
        }

        if (selection.ShowFans)
        {
            var fans = data?.GetSeriesKeys()
                .Where(key => key.Type is SensorType.FanSpeed or SensorType.GpuFan)
                .Take(4)
                .ToArray() ?? [];
            if (fans.Length == 0)
                rows.Add(new OverlayMetricViewModel("VENTOINHA", "—"));
            else
                foreach (var fan in fans)
                    rows.Add(new OverlayMetricViewModel(
                        fan.Type == SensorType.GpuFan ? "GPU FAN" : fan.Index > 0 ? $"FAN {fan.Index}" : "FAN",
                        fan.Type == SensorType.GpuFan ? $"{data!.GetValue(fan):F0}%" : $"{data!.GetValue(fan):F0} RPM"));
        }

        if (selection.ShowMotherboard)
        {
            var temperatures = data?.GetSeriesKeys()
                .Where(key => key.Type is SensorType.MotherboardTemp or SensorType.VrmTemperature)
                .Select(key => data!.GetValue(key))
                .ToArray() ?? [];
            rows.Add(new OverlayMetricViewModel("PLACA", temperatures.Length > 0
                ? $"{temperatures.Max():F0} °C"
                : "—"));
        }

        if (selection.ShowFps)
        {
            var fps = TryValue(data, SensorType.Fps, out var fpsValue) ? $"{fpsValue:F0} FPS" : "— FPS";
            var frametime = TryValue(data, SensorType.FrametimeMs, out var frameValue) ? $"{frameValue:F2} ms" : "— ms";
            rows.Add(new OverlayMetricViewModel("JOGO", $"{fps} · {frametime}"));
        }

        return rows;
    }

    private static bool TryValue(SensorData? data, SensorType type, out double value)
    {
        if (data is not null)
        {
            var key = data.GetSeriesKeys().FirstOrDefault(candidate => candidate.Type == type);
            if (key.Type == type && (key.IsPrimary ? data.Values.ContainsKey(type) : data.Series.ContainsKey(key)))
            {
                value = data.GetValue(key);
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static string RateValue(double bytesPerSecond) => bytesPerSecond >= 1024 * 1024
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
    bool ShowFps);
