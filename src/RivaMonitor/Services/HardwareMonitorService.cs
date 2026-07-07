using System.IO;
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

            foreach (var hardware in _computer.Hardware)
            {
                foreach (var subhardware in hardware.SubHardware)
                    subhardware.Update();

                foreach (var sensor in hardware.Sensors)
                    MapSensor(sensor, data);
            }

            data.GpuProcesses = GetGpuProcesses();
            _current = data;

            try
            {
                SensorUpdated?.Invoke(data);
            }
            catch
            {
            }
        }
        catch
        {
        }
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
            case LhmSensorType.Temperature when sensor.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase):
                data.Values[Models.SensorType.GpuHotSpot] = val;
                data.Labels[Models.SensorType.GpuHotSpot] = label;
                break;
            case LhmSensorType.Temperature:
                data.Values[Models.SensorType.GpuTemperature] = val;
                data.Labels[Models.SensorType.GpuTemperature] = label;
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
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                return list;

            using var searcher = new System.Management.ManagementObjectSearcher(
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
        catch
        {
        }

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
