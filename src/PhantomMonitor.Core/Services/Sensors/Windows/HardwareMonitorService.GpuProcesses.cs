using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using PhantomMonitor.Models;
using LibreHardwareMonitor.Hardware;
using SType = PhantomMonitor.Models.SensorType;

namespace PhantomMonitor.Services;

public sealed partial class HardwareMonitorService
{
    private List<GpuProcessInfo> GetGpuProcesses()
    {
        if (DateTime.UtcNow - _lastGpuProcesses < GpuProcessInterval)
            return _cachedGpuProcesses;

        _lastGpuProcesses = DateTime.UtcNow;
        var result = new List<GpuProcessInfo>();

        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return _cachedGpuProcesses = result;

            // Por PID: soma instâncias da mesma engine e usa a engine mais carregada (cada engine vai a 100%).
            var perPid = new Dictionary<int, Dictionary<string, double>>();

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            using var results = searcher.Get(); // coleção COM do WMI também precisa ser descartada (query a cada 3 s)
            foreach (var obj in results)
            {
                using (obj)
                {
                    var match = GpuEngineRegex.Match(obj["Name"]?.ToString() ?? "");
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var pid)) continue;

                    var usage = Convert.ToDouble(obj["UtilizationPercentage"] ?? 0);
                    var engine = match.Groups[2].Value;

                    if (!perPid.TryGetValue(pid, out var engines))
                        perPid[pid] = engines = [];
                    engines[engine] = engines.GetValueOrDefault(engine) + usage;
                }
            }

            foreach (var (pid, engines) in perPid)
            {
                var usage = engines.Values.Max();
                if (usage <= 0) continue;
                result.Add(new GpuProcessInfo { Name = ResolveProcessName(pid), Usage = Math.Min(usage, 100) });
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("WMI.GpuEngine", ex);
        }

        // Processos que já não existem não precisam continuar em cache.
        if (_pidNames.Count > 512) _pidNames.Clear();

        return _cachedGpuProcesses = result.OrderByDescending(p => p.Usage).Take(10).ToList();
    }

    private string ResolveProcessName(int pid)
    {
        if (_pidNames.TryGetValue(pid, out var name)) return name;

        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName;
        }
        catch
        {
            name = $"PID {pid}";
        }

        return _pidNames[pid] = name;
    }
}
