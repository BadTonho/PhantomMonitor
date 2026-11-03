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
        double vramTemp = data.GetValue(SensorType.GpuVramTemperature);
        if (vramTemp > 0) parts.Add($"MEM {vramTemp:F0}°C");
        return parts.Count > 0 ? string.Join("  ", parts) : null;
    }

    /// <summary>Badge de frequência e timings da RAM (ex.: "3000 MHz · CL15").</summary>
    public static string BuildRamSpecBadge(SensorData data)
    {
        var parts = new List<string>(2);
        var freq = data.GetValue(SensorType.RamFrequency);
        if (freq > 0) parts.Add($"{freq:F0} MHz");
        if (!string.IsNullOrWhiteSpace(data.RamTimings))
            parts.Add(data.RamTimings);
        else if (data.GetValue(SensorType.RamCasLatency) is var cl and > 0)
            parts.Add($"CL{cl:F0}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "DRAM";
    }

    /// <summary>Detalhes de alocação da RAM: comprometida, sistema e jogo.</summary>
    public static string BuildRamExtraDetail(SensorData data)
    {
        var parts = new List<string>(3);
        var committed = data.GetValue(SensorType.RamCommitted);
        var commitLimit = data.GetValue(SensorType.RamCommitLimit);
        if (committed > 0)
        {
            parts.Add(commitLimit > 0
                ? $"Comprometida {committed:F1}/{commitLimit:F1} GB"
                : $"Comprometida {committed:F1} GB");
        }
        if (data.Values.ContainsKey(SensorType.RamSystemUsed))
            parts.Add($"Sistema {data.GetValue(SensorType.RamSystemUsed):F1} GB");
        if (data.Values.ContainsKey(SensorType.RamGameUsed))
            parts.Add($"Jogo {data.GetValue(SensorType.RamGameUsed):F1} GB");
        return parts.Count > 0 ? string.Join("  ·  ", parts) : "—";
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
    public static string FormatSeries(SensorKey key, double value) => FormatSeries(key, value, showZero: false);

    /// <summary>Formats an instance series, optionally preserving a valid zero reading.</summary>
    public static string FormatSeries(SensorKey key, double value, bool showZero)
    {
        if (!showZero && value <= 0) return "--";
        return key.Type switch
        {
            SensorType.CpuTemperature or SensorType.GpuTemperature or SensorType.GpuHotSpot or
            SensorType.GpuVramTemperature or SensorType.MotherboardTemp or SensorType.VrmTemperature or
            SensorType.RamTemperature or
            SensorType.DiskTemperature => $"{value:F0}°C",

            SensorType.CpuTotal or SensorType.CpuPerCore or SensorType.GpuCore or SensorType.GpuMemory or
            SensorType.RamUsage or SensorType.DiskUsage or SensorType.DiskHealth or SensorType.GpuFan => $"{value:F1}%",

            SensorType.CpuClock or SensorType.GpuCoreClock or SensorType.GpuMemoryClock => FormatClock(value),

            SensorType.RamFrequency => $"{value:F0} MHz",

            SensorType.RamCasLatency => $"CL{value:F0}",

            SensorType.RamTrcd or SensorType.RamTrp or SensorType.RamTras => $"{value:F0} clk",

            SensorType.CpuVoltage or SensorType.GpuVoltage or SensorType.RailVoltage => $"{value:F2} V",

            SensorType.CpuPower or SensorType.GpuPower => $"{value:F0} W",

            SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal => $"{value / 1024.0:F1} GB",

            SensorType.RamAvailable or SensorType.RamUsed or SensorType.RamTotal or
            SensorType.RamCommitted or SensorType.RamCommitLimit or SensorType.RamSystemUsed or SensorType.RamGameUsed => $"{value:F1} GB",

            SensorType.DiskTotalRead or SensorType.DiskTotalWritten => $"{value:F0} GB",

            SensorType.DiskRead or SensorType.DiskWrite or SensorType.NetworkUp or SensorType.NetworkDown =>
                FormatBytesPerSec(value),

            SensorType.NetworkUpTotal or SensorType.NetworkDownTotal => FormatBytes(value),

            SensorType.FanSpeed => showZero && value == 0 ? "0 RPM" : FormatFan(value),

            SensorType.Fps or SensorType.FpsAvg or SensorType.FpsMin or SensorType.Fps1Low or
            SensorType.Fps01Low or SensorType.FpsMax => $"{value:F0}",

            SensorType.FrametimeMs => $"{value:F1} ms",

            // Contadores inteiros (WHEA): "3 err" — não mostra "3.0" do fallback F1.
            SensorType.WheaErrorCount => $"{value:F0} err",

            _ => value.ToString("F1")
        };
    }
}
