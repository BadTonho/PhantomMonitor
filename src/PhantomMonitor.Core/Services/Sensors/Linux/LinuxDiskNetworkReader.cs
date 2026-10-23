using System.Globalization;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.Services.Linux;

internal sealed class LinuxDiskNetworkReader
{

    private long _prevDiskReadSectors;
    private long _prevDiskWriteSectors;
    private DateTime _prevDiskTimestamp = DateTime.UtcNow;
    private readonly Dictionary<string, int> _diskSlots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Read, long Write)> _prevPerDisk = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _nicSlots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Rx, long Tx)> _prevPerNic = new(StringComparer.Ordinal);
    private DateTime _prevNetTimestamp = DateTime.MinValue;
    private DateTime? _lastNetSampleUtc;
    private double _netDownSessionBytes;
    private double _netUpSessionBytes;
    internal void ReadDiskStats(SensorData data)
    {
        try
        {
            if (!File.Exists("/proc/diskstats")) return;

            var now = DateTime.UtcNow;
            var elapsedSec = (now - _prevDiskTimestamp).TotalSeconds;

            long totalReadSectors = 0;
            long totalWriteSectors = 0;
            var devices = new List<string>();

            foreach (var line in File.ReadLines("/proc/diskstats"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // Coluna 2: nome do dispositivo (nvme0n1, sda, etc.)
                if (parts.Length < 10) continue;
                string devName = parts[2];
                // Filtrar apenas discos principais, ignorando partições (sda1) e loopback
                if (!IsPhysicalDisk(devName)) continue;
                if (!long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rSectors) ||
                    !long.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var wSectors))
                    continue;

                devices.Add(devName);
                totalReadSectors += rSectors;
                totalWriteSectors += wSectors;

                // Série por disco: slot estável por nome na ordem de enumeração.
                if (!_diskSlots.TryGetValue(devName, out var slot)) slot = _diskSlots[devName] = _diskSlots.Count + 1;

                if (elapsedSec > 0 && _prevPerDisk.TryGetValue(devName, out var prev))
                {
                    double readBps = Math.Max(0, rSectors - prev.Read) * 512.0 / elapsedSec;
                    double writeBps = Math.Max(0, wSectors - prev.Write) * 512.0 / elapsedSec;
                    data.SetValue(new SensorKey(SensorType.DiskRead, slot), readBps, devName);
                    data.SetValue(new SensorKey(SensorType.DiskWrite, slot), writeBps, devName);
                }
                _prevPerDisk[devName] = (rSectors, wSectors);
            }

            var elapsed = (now - _prevDiskTimestamp).TotalSeconds;
            if (elapsed > 0 && _prevDiskReadSectors > 0)
            {
                long deltaRead = Math.Max(0, totalReadSectors - _prevDiskReadSectors);
                long deltaWrite = Math.Max(0, totalWriteSectors - _prevDiskWriteSectors);

                // 1 setor = 512 bytes
                double readBytesSec = (deltaRead * 512.0) / elapsed;
                double writeBytesSec = (deltaWrite * 512.0) / elapsed;

                data.SetValue(SensorType.DiskRead, readBytesSec, "Discos (somatório)");
                data.SetValue(SensorType.DiskWrite, writeBytesSec, "Discos (somatório)");

                // Estimativa de atividade de disco
                double totalThroughputMB = (readBytesSec + writeBytesSec) / (1024.0 * 1024.0);
                double usage = Math.Min(100.0, (totalThroughputMB / 500.0) * 100.0);
                data.SetValue(SensorType.DiskUsage, usage, "Discos (estimado)");
            }

            _prevDiskReadSectors = totalReadSectors;
            _prevDiskWriteSectors = totalWriteSectors;
            _prevDiskTimestamp = now;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Disk", ex);
        }
    }

    /// <summary>Discos físicos principais (sda..sdz, nvme0n1); ignora partições/loop/ram/usb flash?</summary>

    private static bool IsPhysicalDisk(string devName) =>
        (devName.StartsWith("sd", StringComparison.Ordinal) && devName.Length == 3 && char.IsAsciiLetter(devName[2])) ||
        (devName.StartsWith("nvme", StringComparison.Ordinal) && devName.Length > 4 && char.IsAsciiDigit(devName[4]) && devName.Contains('n') && !devName.Contains('p'));

    /// <summary>
    /// Lê tráfego de rede por interface a partir de /proc/net/dev (↑/↓ + totais da sessão).
    /// </summary>

    internal void ReadNetworkStats(SensorData data)
    {
        try
        {
            const string netDevPath = "/proc/net/dev";
            if (!File.Exists(netDevPath)) return;

            var now = DateTime.UtcNow;
            if (_prevNetTimestamp == DateTime.MinValue) _prevNetTimestamp = now;
            var elapsedSec = (now - _prevNetTimestamp).TotalSeconds;

            var interfaces = new List<(string Name, long Rx, long Tx)>();
            foreach (var line in File.ReadLines(netDevPath))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;

                var ifName = line[..colon].Trim();
                if (ifName == "lo") continue; // loopback polui totais sem tráfego real de NIC

                var fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 9) continue;
                if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rx) ||
                    !long.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tx))
                    continue;

                interfaces.Add((ifName, rx, tx));
            }
            _prevNetTimestamp = now;
            if (interfaces.Count == 0) return;

            double downBps = 0;
            double upBps = 0;
            foreach (var (name, rx, tx) in interfaces)
            {
                if (!_nicSlots.TryGetValue(name, out var slot)) slot = _nicSlots[name] = _nicSlots.Count + 1;

                if (elapsedSec > 0 && _prevPerNic.TryGetValue(name, out var prev))
                {
                    double rxBps = Math.Max(0, rx - prev.Rx) / elapsedSec;
                    double txBps = Math.Max(0, tx - prev.Tx) / elapsedSec;
                    downBps += rxBps;
                    upBps += txBps;
                    data.SetValue(new SensorKey(SensorType.NetworkDown, slot), rxBps, name);
                    data.SetValue(new SensorKey(SensorType.NetworkUp, slot), txBps, name);
                }
                _prevPerNic[name] = (rx, tx);
            }

            if (downBps > 0 || upBps > 0)
            {
                data.SetValue(SensorType.NetworkDown, downBps, "Rede ↓ (todas as interfaces)");
                data.SetValue(SensorType.NetworkUp, upBps, "Rede ↑ (todas as interfaces)");

                // Totais da sessão: velocidade observada x tempo desde a amostra anterior.
                if (_lastNetSampleUtc is { } last)
                {
                    var dt = (now - last).TotalSeconds;
                    _netDownSessionBytes += downBps * dt;
                    _netUpSessionBytes += upBps * dt;
                }
                _lastNetSampleUtc = now;

                data.SetValue(SensorType.NetworkDownTotal, _netDownSessionBytes, "Total recebido (sessão)");
                data.SetValue(SensorType.NetworkUpTotal, _netUpSessionBytes, "Total enviado (sessão)");
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Network", ex);
        }
    }
}
