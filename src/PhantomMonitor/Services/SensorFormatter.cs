using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Formatação compartilhada de métricas para a UI (cards da janela principal e overlay OSD).
/// Mantém o estilo consistente entre as duas superfícies de exibição.
/// </summary>
public static class SensorFormatter
{
    public static string FormatBytes(double bytes)
    {
        if (bytes < 1024) return $"{bytes:F0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1} MB";
        return $"{bytes / (1024 * 1024 * 1024):F2} GB";
    }

    public static string FormatBytesPerSec(double bytesPerSec) => FormatBytes(bytesPerSec) + "/s";

    public static string FormatClock(double mhz) => mhz >= 1000 ? $"{mhz / 1000:F2} GHz" : $"{mhz:F0} MHz";

    /// <summary>Ventoinhas reportam % (GPUs NVIDIA) ou RPM (SuperIO/AMD); valores baixos são tratados como %.</summary>
    public static string FormatFan(double value)
    {
        if (value <= 0) return "--";
        return value <= 100 ? $"{value:F0}%" : $"{value:F0} RPM";
    }

    /// <summary>Detalhes de CPU: clock, potência e voltagem (somente os disponíveis).</summary>
    public static string? BuildCpuDetail(SensorData data)
    {
        var parts = new List<string>(3);
        double clock = data.GetValue(SensorType.CpuClock);
        if (clock > 0) parts.Add(FormatClock(clock));
        double power = data.GetValue(SensorType.CpuPower);
        if (power > 0) parts.Add($"{power:F0} W");
        double voltage = data.GetValue(SensorType.CpuVoltage);
        if (voltage > 0) parts.Add($"{voltage:F3} V");
        return parts.Count > 0 ? string.Join("  ", parts) : null;
    }

    /// <summary>Detalhes de GPU: VRAM usada/total, clock, potência e hotspot (somente os disponíveis).</summary>
    public static string? BuildGpuDetail(SensorData data)
    {
        var parts = new List<string>(4);
        double usedMb = data.GetValue(SensorType.GpuMemoryUsed);
        double totalMb = data.GetValue(SensorType.GpuMemoryTotal);
        if (usedMb > 0)
        {
            double usedGb = usedMb / 1024.0;
            parts.Add(totalMb > 0
                ? $"VRAM {usedGb:F1}/{totalMb / 1024.0:F0} GB"
                : $"VRAM {usedGb:F1} GB");
        }
        double clock = data.GetValue(SensorType.GpuCoreClock);
        if (clock > 0) parts.Add(FormatClock(clock));
        double memClock = data.GetValue(SensorType.GpuMemoryClock);
        if (memClock > 0) parts.Add(FormatClock(memClock));
        double power = data.GetValue(SensorType.GpuPower);
        if (power > 0) parts.Add($"{power:F0} W");
        double voltage = data.GetValue(SensorType.GpuVoltage);
        if (voltage > 0) parts.Add($"{voltage:F3} V");
        double hotspot = data.GetValue(SensorType.GpuHotSpot);
        if (hotspot > 0) parts.Add($"HS {hotspot:F0}°C");
        return parts.Count > 0 ? string.Join("  ", parts) : null;
    }

    /// <summary>Linha de ventoinhas dos cards: GPU e sistema.</summary>
    public static string BuildFanSummary(SensorData data)
    {
        double gpu = data.GetValue(SensorType.GpuFan);
        double system = data.GetValue(SensorType.FanSpeed);
        if (gpu <= 0 && system <= 0) return "Ventoinhas: --";
        return $"Ventoinhas: GPU {FormatFan(gpu)} | Sistema {FormatFan(system)}";
    }

    /// <summary>Formata o valor de uma série arbitraria (linhas por instância no OSD/inventário).</summary>
    public static string FormatSeries(SensorKey key, double value)
    {
        if (value <= 0) return "--";
        return key.Type switch
        {
            SensorType.CpuTemperature or SensorType.GpuTemperature or SensorType.GpuHotSpot or
            SensorType.MotherboardTemp or SensorType.VrmTemperature or SensorType.RamTemperature or
            SensorType.DiskTemperature => $"{value:F0}°C",

            SensorType.CpuTotal or SensorType.CpuPerCore or SensorType.GpuCore or SensorType.GpuMemory or
            SensorType.RamUsage or SensorType.DiskUsage or SensorType.DiskHealth or SensorType.GpuFan => $"{value:F1}%",

            SensorType.CpuClock or SensorType.GpuCoreClock or SensorType.GpuMemoryClock => FormatClock(value),

            SensorType.CpuVoltage or SensorType.GpuVoltage or SensorType.RailVoltage => $"{value:F2} V",

            SensorType.CpuPower or SensorType.GpuPower => $"{value:F0} W",

            SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal => $"{value / 1024.0:F1} GB",

            SensorType.RamAvailable or SensorType.RamUsed or SensorType.RamTotal => $"{value:F1} GB",

            SensorType.DiskTotalRead or SensorType.DiskTotalWritten => $"{value:F0} GB",

            SensorType.DiskRead or SensorType.DiskWrite or SensorType.NetworkUp or SensorType.NetworkDown =>
                FormatBytesPerSec(value),

            SensorType.NetworkUpTotal or SensorType.NetworkDownTotal => FormatBytes(value),

            SensorType.FanSpeed => FormatFan(value),

            SensorType.Fps or SensorType.FpsAvg or SensorType.FpsMin or SensorType.Fps1Low or
            SensorType.Fps01Low or SensorType.FpsMax => $"{value:F0}",

            SensorType.FrametimeMs => $"{value:F1} ms",

            _ => value.ToString("F1")
        };
    }
}
