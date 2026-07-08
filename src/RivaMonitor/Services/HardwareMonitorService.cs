using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using RivaMonitor.Models;
using LhmSensorType = LibreHardwareMonitor.Hardware.SensorType;

namespace RivaMonitor.Services;

public sealed class HardwareMonitorService : ISensorService
{
    private readonly Computer _computer;
    private readonly Timer _timer;
    private readonly int _updateIntervalMs;
    private SensorData _current = new();

    public event Action<SensorData>? SensorUpdated;
    public bool IsRunning { get; private set; }

    public HardwareMonitorService(int updateIntervalMs = 1000)
    {
        _updateIntervalMs = updateIntervalMs;
        _timer = new Timer(_ => Update(), null, Timeout.Infinite, Timeout.Infinite);

        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true
        };
    }

    public void Start()
    {
        _computer.Open();
        IsRunning = true;
        _timer.Change(0, _updateIntervalMs);
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        IsRunning = false;
        _computer.Close();
    }

    public SensorData GetCurrentSnapshot() => _current;

    private void Update()
    {
        try
        {
            _computer.Accept(new UpdateVisitor());
            var data = new SensorData();
            var debugInfo = new List<string>();

            foreach (var hardware in _computer.Hardware)
            {
                debugInfo.Add($"[HWR] {hardware.HardwareType}: {hardware.Name}");

                foreach (var subhardware in hardware.SubHardware)
                {
                    subhardware.Update();
                    debugInfo.Add($"  [SUB] {subhardware.HardwareType}: {subhardware.Name}");
                }

                foreach (var sensor in hardware.Sensors)
                {
                    MapSensor(sensor, data);
                    var val = sensor.Value?.ToString("F2") ?? "null";
                    debugInfo.Add($"  [{hardware.HardwareType}] {sensor.Name} ({sensor.SensorType}) = {val}");
                }
            }

            data.SensorDebugInfo = debugInfo;

            // WMI: CPU Temperature (tenta 3 métodos diferentes)
            double cpuTempValue = 0;
            string cpuTempSource = "";

            try
            {
                cpuTempValue = ReadCpuTempFromThermalZone();
                cpuTempSource = "ThermalZone";
                debugInfo.Add($"[WMI] ThermalZone = {cpuTempValue:F1}°C");
            }
            catch (Exception ex) { debugInfo.Add($"[WMI] ThermalZone error: {ex.Message}"); }

            if (cpuTempValue <= 0)
            {
                try
                {
                    cpuTempValue = ReadCpuTempFromPerfCounter();
                    cpuTempSource = "PerfCounter";
                    debugInfo.Add($"[WMI] PerfCounter = {cpuTempValue:F1}°C");
                }
                catch (Exception ex) { debugInfo.Add($"[WMI] PerfCounter error: {ex.Message}"); }
            }

            if (cpuTempValue <= 0)
            {
                try
                {
                    cpuTempValue = ReadCpuTempFromWmiProbe();
                    cpuTempSource = "TemperatureProbe";
                    debugInfo.Add($"[WMI] TemperatureProbe = {cpuTempValue:F1}°C");
                }
                catch (Exception ex) { debugInfo.Add($"[WMI] TemperatureProbe error: {ex.Message}"); }
            }

            if (cpuTempValue <= 0)
            {
                try
                {
                    cpuTempValue = ReadCpuTempFromRegistry();
                    cpuTempSource = "Registry";
                    debugInfo.Add($"[REG] CPU Temperature = {cpuTempValue:F1}°C");
                }
                catch (Exception ex) { debugInfo.Add($"[REG] CPU Temp error: {ex.Message}"); }
            }

            if (cpuTempValue <= 0)
            {
                try
                {
                    cpuTempValue = ReadCpuTempFromWmiBattery();
                    cpuTempSource = "Battery";
                    debugInfo.Add($"[WMI] BatteryTemp = {cpuTempValue:F1}°C");
                }
                catch (Exception ex) { debugInfo.Add($"[WMI] Battery error: {ex.Message}"); }
            }

            if (cpuTempValue > 0)
            {
                data.Values[Models.SensorType.CpuTemperature] = cpuTempValue;
                data.Labels[Models.SensorType.CpuTemperature] = $"CPU ({cpuTempSource})";
            }

            // WMI: RAM info
            try
            {
                var (avail, total) = ReadRamFromWmi();
                debugInfo.Add($"[WMI] RAM Available = {avail:F1} GB");
                debugInfo.Add($"[WMI] RAM Total = {total:F1} GB");
                if (avail > 0)
                {
                    data.Values[Models.SensorType.RamAvailable] = avail;
                    data.Labels[Models.SensorType.RamAvailable] = "RAM Disponível (WMI)";
                }
                if (total > 0)
                {
                    data.Values[Models.SensorType.RamTotal] = total;
                    data.Labels[Models.SensorType.RamTotal] = "RAM Total (WMI)";
                }
            }
            catch (Exception ex) { debugInfo.Add($"[WMI] RAM error: {ex.Message}"); }

            // Fallback: pegar RAM disponível do desempenho do Windows se WMI falhou
            if (!data.Values.ContainsKey(Models.SensorType.RamAvailable) || data.Values[Models.SensorType.RamAvailable] == 0)
            {
                try
                {
                    var perf = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes");
                    var availMB = perf.NextValue();
                    debugInfo.Add($"[PERF] RAM Available = {availMB / 1024.0:F1} GB");
                    data.Values[Models.SensorType.RamAvailable] = availMB / 1024.0;
                    data.Labels[Models.SensorType.RamAvailable] = "RAM Disponível (Perf)";
                }
                catch (Exception ex) { debugInfo.Add($"[PERF] RAM error: {ex.Message}"); }
            }

            data.SensorDebugInfo = debugInfo;

            data.GpuProcesses = GetGpuProcesses();
            _current = data;

            try { SensorUpdated?.Invoke(data); } catch { }
        }
        catch { }
    }

    private static double ReadCpuTempFromThermalZone()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM MSAcpi_ThermalZoneTemperature");
        foreach (var obj in searcher.Get())
        {
            var temp = Convert.ToDouble(obj["CurrentTemperature"] ?? 0);
            if (temp > 0)
                return (temp - 2731.5) / 10.0;
        }
        return 0;
    }

    private static double ReadCpuTempFromPerfCounter()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
        foreach (var obj in searcher.Get())
        {
            var temp = Convert.ToDouble(obj["Temperature"] ?? 0);
            if (temp > 0)
                return temp / 100.0;
        }
        return 0;
    }

    private static double ReadCpuTempFromWmiProbe()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_TemperatureProbe");
        foreach (var obj in searcher.Get())
        {
            var temp = Convert.ToDouble(obj["CurrentReading"] ?? 0);
            if (temp > 0)
                return temp / 10.0;
        }
        return 0;
    }

    private static double ReadCpuTempFromRegistry()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"HARDWARE\ACPI\DSDT\ACPI0004\CPU0\_TMP");
        if (key != null)
        {
            var val = key.GetValue("_TMP");
            if (val is byte[] bytes && bytes.Length >= 4)
            {
                var raw = BitConverter.ToUInt32(bytes, 0);
                return (raw - 2731.5) / 10.0;
            }
        }
        return 0;
    }

    private static double ReadCpuTempFromWmiBattery()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_Battery");
        foreach (var obj in searcher.Get())
        {
            var temp = Convert.ToDouble(obj["Temperature"] ?? 0);
            if (temp > 0)
                return temp / 10.0;
        }
        return 0;
    }

    private static (double availableGB, double totalGB) ReadRamFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                var free = Convert.ToDouble(obj["FreePhysicalMemory"] ?? 0);
                var total = Convert.ToDouble(obj["TotalVisibleMemorySize"] ?? 0);
                return (free / 1024.0 / 1024.0, total / 1024.0 / 1024.0);
            }
        }
        catch { }
        return (0, 0);
    }

    private static void MapSensor(ISensor sensor, SensorData data)
    {
        var val = sensor.Value;
        if (!val.HasValue) return;

        double v = val.Value;
        var label = $"{sensor.Name} ({sensor.SensorType})";

        switch (sensor.Hardware.HardwareType)
        {
            case HardwareType.Cpu:
                MapCpuSensor(sensor, v, label, data);
                break;
            case HardwareType.GpuNvidia:
            case HardwareType.GpuAmd:
            case HardwareType.GpuIntel:
                MapGpuSensor(sensor, v, label, data);
                break;
            case HardwareType.Memory:
                MapMemorySensor(sensor, v, label, data);
                break;
            case HardwareType.Storage:
                MapStorageSensor(sensor, v, label, data);
                break;
            case HardwareType.Network:
                MapNetworkSensor(sensor, v, label, data);
                break;
            case HardwareType.Motherboard:
            case HardwareType.SuperIO:
                MapMotherboardSensor(sensor, v, label, data);
                break;
        }
    }

    private static void MapCpuSensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Load when sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.CpuTotal] = val;
                data.Labels[Models.SensorType.CpuTotal] = label;
                break;
            case LhmSensorType.Load:
                data.Values[Models.SensorType.CpuPerCore] = val;
                data.Labels[Models.SensorType.CpuPerCore] = label;
                break;
            case LhmSensorType.Temperature:
                data.Values[Models.SensorType.CpuTemperature] = val;
                data.Labels[Models.SensorType.CpuTemperature] = label;
                break;
            case LhmSensorType.Clock:
                data.Values[Models.SensorType.CpuClock] = val;
                data.Labels[Models.SensorType.CpuClock] = label;
                break;
            case LhmSensorType.Voltage:
                data.Values[Models.SensorType.CpuVoltage] = val;
                data.Labels[Models.SensorType.CpuVoltage] = label;
                break;
            case LhmSensorType.Power:
                data.Values[Models.SensorType.CpuPower] = val;
                data.Labels[Models.SensorType.CpuPower] = label;
                break;
        }
    }

    private static void MapGpuSensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Load when sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.GpuCore] = val;
                data.Labels[Models.SensorType.GpuCore] = label;
                break;
            case LhmSensorType.Load when sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.GpuMemory] = val;
                data.Labels[Models.SensorType.GpuMemory] = label;
                break;
            case LhmSensorType.Clock when sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.GpuCoreClock] = val;
                data.Labels[Models.SensorType.GpuCoreClock] = label;
                break;
            case LhmSensorType.Clock when sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.GpuMemoryClock] = val;
                data.Labels[Models.SensorType.GpuMemoryClock] = label;
                break;
            case LhmSensorType.Temperature when sensor.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.GpuHotSpot] = val;
                data.Labels[Models.SensorType.GpuHotSpot] = label;
                break;
            case LhmSensorType.Temperature:
                data.Values[Models.SensorType.GpuTemperature] = val;
                data.Labels[Models.SensorType.GpuTemperature] = label;
                break;
            case LhmSensorType.Voltage:
                data.Values[Models.SensorType.GpuVoltage] = val;
                data.Labels[Models.SensorType.GpuVoltage] = label;
                break;
            case LhmSensorType.Fan:
                data.Values[Models.SensorType.GpuFan] = val;
                data.Labels[Models.SensorType.GpuFan] = label;
                break;
            case LhmSensorType.Power:
                data.Values[Models.SensorType.GpuPower] = val;
                data.Labels[Models.SensorType.GpuPower] = label;
                break;
        }
    }

    private static void MapMemorySensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Load:
                data.Values[Models.SensorType.RamUsage] = val;
                data.Labels[Models.SensorType.RamUsage] = label;
                break;
            case LhmSensorType.Data when sensor.Name.Contains("Available", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.RamAvailable] = val / 1024.0;
                data.Labels[Models.SensorType.RamAvailable] = label;
                break;
            case LhmSensorType.Data when sensor.Name.Contains("Used", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.RamUsed] = val / 1024.0;
                data.Labels[Models.SensorType.RamUsed] = label;
                break;
        }
    }

    private static void MapStorageSensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Load:
                data.Values[Models.SensorType.DiskUsage] = val;
                data.Labels[Models.SensorType.DiskUsage] = label;
                break;
            case LhmSensorType.Throughput when sensor.Name.Contains("Read", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.DiskRead] = val;
                data.Labels[Models.SensorType.DiskRead] = label;
                break;
            case LhmSensorType.Throughput when sensor.Name.Contains("Write", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.DiskWrite] = val;
                data.Labels[Models.SensorType.DiskWrite] = label;
                break;
        }
    }

    private static void MapNetworkSensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Throughput when sensor.Name.Contains("Upload", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.NetworkUp] = val;
                data.Labels[Models.SensorType.NetworkUp] = label;
                break;
            case LhmSensorType.Throughput when sensor.Name.Contains("Download", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.NetworkDown] = val;
                data.Labels[Models.SensorType.NetworkDown] = label;
                break;
        }
    }

    private static void MapMotherboardSensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Temperature:
                data.Values[Models.SensorType.MotherboardTemp] = val;
                data.Labels[Models.SensorType.MotherboardTemp] = label;
                break;
            case LhmSensorType.Fan:
                data.Values[Models.SensorType.FanSpeed] = val;
                data.Labels[Models.SensorType.FanSpeed] = label;
                break;
        }
    }

    private static List<GpuProcessInfo> GetGpuProcesses()
    {
        var list = new List<GpuProcessInfo>();
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return list;

            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                var usage = Convert.ToDouble(obj["PercentOfTime"] ?? 0);
                var parts = name.Split('_');
                var processName = parts.Length > 2 ? parts[^1] : name;

                var existing = list.Find(p =>
                    p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    existing.Usage += usage;
                else
                    list.Add(new GpuProcessInfo { Name = processName, Usage = usage });
            }
        }
        catch { }

        return list.OrderByDescending(p => p.Usage).Take(10).ToList();
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
        _computer.Close();
    }
}

internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware) { hardware.Update(); foreach (var sh in hardware.SubHardware) sh.Update(); }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}
