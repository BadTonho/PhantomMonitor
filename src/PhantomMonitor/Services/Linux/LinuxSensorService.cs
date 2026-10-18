using System.Globalization;
using System.IO;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services.Linux;

/// <summary>
/// Provedor nativo de telemetria e sensores para Linux.
/// Lê diretamente do kernel Linux via procfs (/proc) e sysfs (/sys/class/hwmon, /sys/class/drm),
/// sem requerer drivers proprietários nem utilitários de terceiros.
/// </summary>
public sealed class LinuxSensorService : ISensorService
{
    private readonly Timer _timer;
    private readonly int _updateIntervalMs;
    private readonly LinuxFpsMonitor _fpsMonitor = new();
    private SensorData _current = new();

    // CPU calculation state
    private long _prevCpuTotal;
    private long _prevCpuIdle;

    // Disk calculation state
    private long _prevDiskReadSectors;
    private long _prevDiskWriteSectors;
    private DateTime _prevDiskTimestamp = DateTime.UtcNow;

    public event Action<SensorData>? SensorUpdated;
    public bool IsRunning { get; private set; }

    public LinuxSensorService(int updateIntervalMs = 1000)
    {
        _updateIntervalMs = updateIntervalMs;
        _timer = new Timer(_ => Update(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        IsRunning = true;
        _fpsMonitor.Start();
        _timer.Change(0, _updateIntervalMs);
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _fpsMonitor.Stop();
        IsRunning = false;
    }

    public SensorData GetCurrentSnapshot() => _current;

    private void Update()
    {
        try
        {
            var data = new SensorData
            {
                Timestamp = DateTime.UtcNow
            };

            ReadCpuStats(data);
            ReadMemInfo(data);
            ReadHwmonSensors(data);
            ReadDiskStats(data);
            ReadGpuSysfs(data);

            if (_fpsMonitor.HasData)
            {
                data.Values[SensorType.Fps] = _fpsMonitor.Fps;
                data.Values[SensorType.FrametimeMs] = _fpsMonitor.FrametimeMs;
                data.Values[SensorType.FpsAvg] = _fpsMonitor.FpsAvg;
                data.Values[SensorType.FpsMin] = _fpsMonitor.FpsMin;
                data.Values[SensorType.Fps1Low] = _fpsMonitor.Fps1Low;
                data.FrametimeHistory = _fpsMonitor.FrametimeHistory.ToArray();
            }

            _current = data;
            SensorUpdated?.Invoke(data);
        }
        catch
        {
            // Proteção contra leitura em momentos de desligamento ou falta de arquivos
        }
    }

    /// <summary>
    /// Lê a utilização da CPU a partir de /proc/stat
    /// </summary>
    private void ReadCpuStats(SensorData data)
    {
        try
        {
            if (!File.Exists("/proc/stat")) return;

            var lines = File.ReadLines("/proc/stat");
            var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu "));
            if (cpuLine == null) return;

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

            // Ler frequência da CPU (scaling_cur_freq do core 0 em kHz)
            const string cpuFreqPath = "/sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq";
            if (File.Exists(cpuFreqPath))
            {
                if (long.TryParse(File.ReadAllText(cpuFreqPath).Trim(), out var khz))
                {
                    data.SetValue(SensorType.CpuClock, khz / 1000.0); // MHz
                }
            }
        }
        catch
        {
            // Ignora falhas transitórias
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
        catch
        {
            // Ignora falhas de leitura
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
    private void ReadHwmonSensors(SensorData data)
    {
        try
        {
            const string hwmonBase = "/sys/class/hwmon";
            if (!Directory.Exists(hwmonBase)) return;

            foreach (var dir in Directory.GetDirectories(hwmonBase))
            {
                string name = "";
                var namePath = Path.Combine(dir, "name");
                if (File.Exists(namePath))
                {
                    name = File.ReadAllText(namePath).Trim().ToLowerInvariant();
                }

                bool isCpuHwmon = name.Contains("coretemp") || name.Contains("k10temp") || name.Contains("zenpower");
                bool isGpuHwmon = name.Contains("amdgpu") || name.Contains("nouveau") || name.Contains("nvidia");

                // Ler temperaturas do diretório
                foreach (var tempFile in Directory.GetFiles(dir, "temp*_input"))
                {
                    if (double.TryParse(File.ReadAllText(tempFile).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var milliDeg))
                    {
                        double deg = milliDeg / 1000.0;

                        if (isCpuHwmon && (data.CpuTemp <= 0 || deg > data.CpuTemp))
                        {
                            data.SetValue(SensorType.CpuTemperature, deg, "CPU Temp");
                        }
                        else if (isGpuHwmon && (data.GpuTemp <= 0 || deg > data.GpuTemp))
                        {
                            data.SetValue(SensorType.GpuTemperature, deg, "GPU Temp");
                        }
                    }
                }

                // Ler RPM de Fan se disponível
                foreach (var fanFile in Directory.GetFiles(dir, "fan*_input"))
                {
                    if (double.TryParse(File.ReadAllText(fanFile).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rpm))
                    {
                        if (isGpuHwmon)
                            data.SetValue(SensorType.GpuFan, rpm);
                    }
                }
            }
        }
        catch
        {
            // Ignora falhas de leitura
        }
    }

    /// <summary>
    /// Lê estatísticas de leitura e escrita de disco a partir de /proc/diskstats
    /// </summary>
    private void ReadDiskStats(SensorData data)
    {
        try
        {
            if (!File.Exists("/proc/diskstats")) return;

            long totalReadSectors = 0;
            long totalWriteSectors = 0;

            foreach (var line in File.ReadLines("/proc/diskstats"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // Coluna 2: nome do dispositivo (nvme0n1, sda, etc.)
                if (parts.Length >= 10)
                {
                    string devName = parts[2];
                    // Filtrar apenas discos principais, ignorando partições numeradas como sda1 ou loopback
                    if (devName.StartsWith("sd") && char.IsLetter(devName[^1]) ||
                        devName.StartsWith("nvme") && devName.Contains("n1") && !devName.Contains("p"))
                    {
                        if (long.TryParse(parts[5], out var rSectors)) totalReadSectors += rSectors;
                        if (long.TryParse(parts[9], out var wSectors)) totalWriteSectors += wSectors;
                    }
                }
            }

            var now = DateTime.UtcNow;
            var elapsedSec = (now - _prevDiskTimestamp).TotalSeconds;

            if (elapsedSec > 0 && _prevDiskReadSectors > 0)
            {
                long deltaRead = Math.Max(0, totalReadSectors - _prevDiskReadSectors);
                long deltaWrite = Math.Max(0, totalWriteSectors - _prevDiskWriteSectors);

                // 1 setor = 512 bytes
                double readBytesSec = (deltaRead * 512.0) / elapsedSec;
                double writeBytesSec = (deltaWrite * 512.0) / elapsedSec;

                data.SetValue(SensorType.DiskRead, readBytesSec);
                data.SetValue(SensorType.DiskWrite, writeBytesSec);

                // Estimativa de atividade de disco
                double totalThroughputMB = (readBytesSec + writeBytesSec) / (1024.0 * 1024.0);
                double usage = Math.Min(100.0, (totalThroughputMB / 500.0) * 100.0);
                data.SetValue(SensorType.DiskUsage, usage);
            }

            _prevDiskReadSectors = totalReadSectors;
            _prevDiskWriteSectors = totalWriteSectors;
            _prevDiskTimestamp = now;
        }
        catch
        {
            // Ignora falhas de disco
        }
    }

    /// <summary>
    /// Lê a utilização de GPU via subsistema DRM (/sys/class/drm)
    /// </summary>
    private void ReadGpuSysfs(SensorData data)
    {
        try
        {
            // AMD GPU Busy Percent
            const string amdGpuBusyPath = "/sys/class/drm/card0/device/gpu_busy_percent";
            if (File.Exists(amdGpuBusyPath))
            {
                if (double.TryParse(File.ReadAllText(amdGpuBusyPath).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var busy))
                {
                    data.SetValue(SensorType.GpuCore, Math.Clamp(busy, 0.0, 100.0), "GPU Core");
                }
            }

            // AMD VRAM Used
            const string amdVramUsedPath = "/sys/class/drm/card0/device/mem_info_vram_used";
            if (File.Exists(amdVramUsedPath))
            {
                if (long.TryParse(File.ReadAllText(amdVramUsedPath).Trim(), out var bytes))
                {
                    data.SetValue(SensorType.GpuMemory, bytes / (1024.0 * 1024.0)); // MB
                }
            }
        }
        catch
        {
            // Ignora falhas de leitura
        }
    }

    public void Dispose()
    {
        Stop();
        _fpsMonitor.Dispose();
        _timer.Dispose();
    }
}
