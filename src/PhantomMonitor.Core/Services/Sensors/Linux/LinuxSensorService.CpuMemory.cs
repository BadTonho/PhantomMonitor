using System.Globalization;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.Services.Linux;

public sealed partial class LinuxSensorService
{
    private void ReadCpuStats(SensorData data)
    {
        try
        {
            if (!File.Exists("/proc/stat")) return;

            // Uma única passada: a leitura duplicada de /proc/stat achava o
            // agregado e depois relia o arquivo todo para os núcleos.
            string? cpuLine = null;
            var perCoreLines = new List<string>(16);
            foreach (var line in File.ReadLines("/proc/stat"))
            {
                if (cpuLine is null && line.StartsWith("cpu ", StringComparison.Ordinal))
                {
                    cpuLine = line;
                    continue;
                }
                if (line.StartsWith("cpu", StringComparison.Ordinal) && line.Length > 3)
                    perCoreLines.Add(line);
            }
            if (cpuLine is null) return;

            var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5)
            {
                long user = long.Parse(parts[1], CultureInfo.InvariantCulture);
                long nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
                long system = long.Parse(parts[3], CultureInfo.InvariantCulture);
                long idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
                long iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;
                long irq = parts.Length > 6 ? long.Parse(parts[6], CultureInfo.InvariantCulture) : 0;
                long softirq = parts.Length > 7 ? long.Parse(parts[7], CultureInfo.InvariantCulture) : 0;
                long steal = parts.Length > 8 ? long.Parse(parts[8], CultureInfo.InvariantCulture) : 0;

                long idleTotal = idle + iowait;
                long nonIdle = user + nice + system + irq + softirq + steal;
                long total = idleTotal + nonIdle;

                long deltaTotal = total - _prevCpuTotal;
                long deltaIdle = idleTotal - _prevCpuIdle;

                if (deltaTotal > 0)
                {
                    double cpuUsage = (1.0 - ((double)deltaIdle / deltaTotal)) * 100.0;
                    data.SetValue(SensorType.CpuTotal, Math.Clamp(cpuUsage, 0.0, 100.0), "CPU Total");
                }

                _prevCpuTotal = total;
                _prevCpuIdle = idleTotal;
            }

            // Uso por núcleo: linhas "cpuN" coletadas acima, com estado por núcleo.
            foreach (var line in perCoreLines)
            {
                var coreToken = line["cpu".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (coreToken.Length == 0 || !int.TryParse(coreToken[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var core))
                    continue;

                var coreParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (coreParts.Length < 5) continue;

                long cUser = long.Parse(coreParts[1], CultureInfo.InvariantCulture);
                long cNice = long.Parse(coreParts[2], CultureInfo.InvariantCulture);
                long cSystem = long.Parse(coreParts[3], CultureInfo.InvariantCulture);
                long cIdle = long.Parse(coreParts[4], CultureInfo.InvariantCulture);
                long cIowait = coreParts.Length > 5 ? long.Parse(coreParts[5], CultureInfo.InvariantCulture) : 0;
                long cIrq = coreParts.Length > 6 ? long.Parse(coreParts[6], CultureInfo.InvariantCulture) : 0;
                long cSoftirq = coreParts.Length > 7 ? long.Parse(coreParts[7], CultureInfo.InvariantCulture) : 0;
                long cSteal = coreParts.Length > 8 ? long.Parse(coreParts[8], CultureInfo.InvariantCulture) : 0;

                long cIdleTotal = cIdle + cIowait;
                long cNonIdle = cUser + cNice + cSystem + cIrq + cSoftirq + cSteal;
                long cTotal = cIdleTotal + cNonIdle;

                if (_prevPerCore.TryGetValue(core, out var prev))
                {
                    long dTotal = cTotal - prev.Total;
                    long dIdle = cIdleTotal - prev.Idle;
                    if (dTotal > 0)
                    {
                        double usage = Math.Clamp(100.0 * (1.0 - (double)dIdle / dTotal), 0, 100);
                        data.SetValue(new SensorKey(SensorType.CpuPerCore, core + 1), usage, $"CPU Core #{core}");
                    }
                }
                _prevPerCore[core] = (cTotal, cIdleTotal);
            }

            // Frequência por núcleo (scaling_cur_freq em kHz). O layout de núcleos
            // com cpufreq é estável na sessão: a contagem é detectada uma única vez
            // (antes: até 512 File.Exists/ReadAllText por tick = O(núcleos) syscalls
            // a cada snapshot). Semântica original mantida: pára no primeiro core
            // sem cpufreq.
            if (_cpuCoreCount is null)
            {
                var count = 0;
                while (count < 512 && File.Exists($"/sys/devices/system/cpu/cpu{count}/cpufreq/scaling_cur_freq"))
                    count++;
                _cpuCoreCount = count;
            }
            for (var core = 0; core < _cpuCoreCount.Value; core++)
            {
                var freqPath = $"/sys/devices/system/cpu/cpu{core}/cpufreq/scaling_cur_freq";
                if (!long.TryParse(File.ReadAllText(freqPath).Trim(), out var coreKhz)) continue;
                data.SetValue(new SensorKey(SensorType.CpuClock, core + 1), coreKhz / 1000.0, $"CPU Core #{core}");
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Cpu", ex);
        }
    }

    /// <summary>
    /// Lê a memória física a partir de /proc/meminfo
    /// </summary>

    private void ReadMemInfo(SensorData data)
    {
        try
        {
            if (!File.Exists("/proc/meminfo")) return;

            long totalKb = 0;
            long availKb = 0;

            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                {
                    totalKb = ParseMemInfoValue(line);
                }
                else if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                {
                    availKb = ParseMemInfoValue(line);
                }

                if (totalKb > 0 && availKb > 0) break;
            }

            if (totalKb > 0)
            {
                long usedKb = totalKb - availKb;
                double totalGb = totalKb / (1024.0 * 1024.0);
                double availGb = availKb / (1024.0 * 1024.0);
                double usedGb = usedKb / (1024.0 * 1024.0);
                double usagePct = ((double)usedKb / totalKb) * 100.0;

                data.SetValue(SensorType.RamTotal, totalGb, "RAM Total");
                data.SetValue(SensorType.RamUsed, usedGb, "RAM Used");
                data.SetValue(SensorType.RamAvailable, availGb, "RAM Available");
                data.SetValue(SensorType.RamUsage, Math.Clamp(usagePct, 0.0, 100.0), "RAM Usage");
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Memory", ex);
        }
    }

    private static long ParseMemInfoValue(string line)
    {
        var parts = line.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length > 1)
        {
            var numStr = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
                return val;
        }
        return 0;
    }

    /// <summary>
    /// Lê temperaturas e sensores através do subsistema hwmon (/sys/class/hwmon/)
    /// </summary>
}
