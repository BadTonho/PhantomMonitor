using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using PhantomMonitor.Models;
using LhmSensorType = LibreHardwareMonitor.Hardware.SensorType;
using SType = PhantomMonitor.Models.SensorType;

namespace PhantomMonitor.Services;

public sealed class HardwareMonitorService : ISensorService
{
    private static readonly TimeSpan WmiTempInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GpuProcessInterval = TimeSpan.FromSeconds(3);
    private static readonly Regex GpuEngineRegex = new(@"pid_(\d+)_.*engtype_(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Computer _computer;
    private readonly Timer _timer;
    private readonly int _updateIntervalMs;
    private readonly FpsMonitor _fpsMonitor = new();

    // Serializa Start/Stop/Update/Dispose (o timer roda em thread do pool).
    private readonly object _sync = new();
    private volatile bool _disposed;
    private SensorData _current = new();

    // Prioridade do sensor escolhido por tipo na rodada atual (acessado só dentro de _sync).
    private readonly Dictionary<SType, int> _priority = [];

    // Fallbacks e caches (evitam consultas WMI pesadas a cada ciclo)
    private PerformanceCounter? _ramCounter;
    private DateTime _lastWmiTemp = DateTime.MinValue;
    private double _cachedWmiTemp;
    private DateTime _lastGpuProcesses = DateTime.MinValue;
    private List<GpuProcessInfo> _cachedGpuProcesses = [];
    private readonly Dictionary<int, string> _pidNames = [];

    public event Action<SensorData>? SensorUpdated;
    public bool IsRunning { get; private set; }

    public HardwareMonitorService(int updateIntervalMs = 1000)
    {
        _updateIntervalMs = Math.Max(100, updateIntervalMs);
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
        lock (_sync)
        {
            if (_disposed || IsRunning) return;

            _computer.Open();
            _fpsMonitor.Start();
            IsRunning = true;
            _timer.Change(0, _updateIntervalMs);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!IsRunning) return;

            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            IsRunning = false;
            _computer.Close();
            _fpsMonitor.Stop();
        }
    }

    public SensorData GetCurrentSnapshot() => _current;

    private void Update()
    {
        // Se o ciclo anterior ainda está rodando (ou Stop/Dispose em curso), pula este tick.
        if (!Monitor.TryEnter(_sync)) return;

        SensorData? data = null;
        try
        {
            if (_disposed || !IsRunning) return;
            data = CollectSnapshot();
            _current = data;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("HardwareMonitorService.Update", ex);
        }
        finally
        {
            Monitor.Exit(_sync);
        }

        if (data == null) return;

        // Fora do lock: handlers lentos não bloqueiam Stop/Dispose.
        try { SensorUpdated?.Invoke(data); }
        catch (Exception ex) { ErrorLog.Write("HardwareMonitorService.SensorUpdated", ex); }
    }

    private SensorData CollectSnapshot()
    {
        _computer.Accept(new UpdateVisitor());
        _priority.Clear();

        var data = new SensorData();
        var debugInfo = new List<string>();
        data.SensorDebugInfo = debugInfo;

        // GPUs integradas primeiro: a GPU dedicada (processada depois) prevalece nos valores.
        foreach (var hardware in _computer.Hardware.OrderBy(h => h.HardwareType == HardwareType.GpuIntel ? 0 : 1))
        {
            ProcessHardware(hardware, data, debugInfo, depth: 0);
        }

        ApplyCpuTempFallback(data, debugInfo);
        ApplyRamFallbacks(data, debugInfo);

        data.GpuProcesses = GetGpuProcesses();

        debugInfo.Add($"[FPS] {_fpsMonitor.DebugInfo}");
        debugInfo.Add($"[FPS] PresentMon: {_fpsMonitor.EtwDiag}");
        debugInfo.Add($"[FPS] PerfCount: {_fpsMonitor.PerfResult}");
        if (_fpsMonitor.HasData)
        {
            data.Values[SType.Fps] = _fpsMonitor.Fps;
            data.Values[SType.FrametimeMs] = _fpsMonitor.FrametimeMs;
            data.Values[SType.FpsAvg] = _fpsMonitor.FpsAvg;
            data.Values[SType.FpsMin] = _fpsMonitor.FpsMin;
            data.Values[SType.Fps1Low] = _fpsMonitor.Fps1Low;
            data.Values[SType.Fps01Low] = _fpsMonitor.Fps01Low;
            data.Values[SType.FpsMax] = _fpsMonitor.FpsMax;
            data.FrametimeHistory = _fpsMonitor.FrametimeHistory.ToArray();
        }

        return data;
    }

    private void ProcessHardware(IHardware hardware, SensorData data, List<string> debugInfo, int depth)
    {
        var indent = new string(' ', depth * 2);
        debugInfo.Add($"{indent}[HWR] {hardware.HardwareType}: {hardware.Name}");

        foreach (var sensor in hardware.Sensors)
        {
            MapSensor(sensor, data);
            var val = sensor.Value?.ToString("F2") ?? "null";
            debugInfo.Add($"{indent}  [{hardware.HardwareType}] {sensor.Name} ({sensor.SensorType}) = {val}");
        }

        // Sensores de placa-mãe/SuperIO ficam em sub-hardware.
        foreach (var sub in hardware.SubHardware)
        {
            ProcessHardware(sub, data, debugInfo, depth + 1);
        }
    }

    // ---------- Fallbacks ----------

    private void ApplyCpuTempFallback(SensorData data, List<string> debugInfo)
    {
        if (data.Values.ContainsKey(SType.CpuTemperature)) return;

        // WMI é caro e raramente funciona: consulta no máximo a cada 10 s e reaproveita o resultado.
        if (DateTime.UtcNow - _lastWmiTemp >= WmiTempInterval)
        {
            _lastWmiTemp = DateTime.UtcNow;
            _cachedWmiTemp = 0;

            try { _cachedWmiTemp = ReadCpuTempFromThermalZone(); }
            catch (Exception ex) { ErrorLog.Write("WMI.ThermalZone", ex); }

            if (_cachedWmiTemp <= 0)
            {
                try { _cachedWmiTemp = ReadCpuTempFromPerfCounter(); }
                catch (Exception ex) { ErrorLog.Write("WMI.ThermalZonePerf", ex); }
            }
        }

        // Faixa plausível: descarta leituras absurdas de zonas térmicas mal reportadas.
        if (_cachedWmiTemp is > 0 and < 150)
        {
            data.Values[SType.CpuTemperature] = _cachedWmiTemp;
            data.Labels[SType.CpuTemperature] = "Zona térmica ACPI (aprox.)";
            debugInfo.Add($"[WMI] ThermalZone = {_cachedWmiTemp:F1}°C");
        }
        else
        {
            debugInfo.Add("[WMI] Temperatura da CPU indisponível (execute como Administrador)");
        }
    }

    private void ApplyRamFallbacks(SensorData data, List<string> debugInfo)
    {
        // LibreHardwareMonitor expõe Memory Used/Available em GB.
        if (data.Values.TryGetValue(SType.RamUsed, out var used) &&
            data.Values.TryGetValue(SType.RamAvailable, out var avail) &&
            !data.Values.ContainsKey(SType.RamTotal))
        {
            data.Values[SType.RamTotal] = used + avail;
            data.Labels[SType.RamTotal] = "RAM Total (Used + Available)";
        }

        if (data.GetValue(SType.RamAvailable) > 0 && data.GetValue(SType.RamTotal) > 0) return;

        try
        {
            var (freeGb, totalGb) = ReadRamFromWmi();
            debugInfo.Add($"[WMI] RAM Available = {freeGb:F1} GB / Total = {totalGb:F1} GB");
            if (freeGb > 0 && data.GetValue(SType.RamAvailable) <= 0)
            {
                data.Values[SType.RamAvailable] = freeGb;
                data.Labels[SType.RamAvailable] = "RAM Disponível (WMI)";
            }
            if (totalGb > 0 && data.GetValue(SType.RamTotal) <= 0)
            {
                data.Values[SType.RamTotal] = totalGb;
                data.Labels[SType.RamTotal] = "RAM Total (WMI)";
            }
        }
        catch (Exception ex) { ErrorLog.Write("WMI.Ram", ex); }

        if (data.GetValue(SType.RamAvailable) <= 0)
        {
            try
            {
                // Contador reutilizado entre ciclos (criar um novo a cada tick vaza handles).
                _ramCounter ??= new PerformanceCounter("Memory", "Available MBytes");
                var availGb = _ramCounter.NextValue() / 1024.0;
                data.Values[SType.RamAvailable] = availGb;
                data.Labels[SType.RamAvailable] = "RAM Disponível (Perf)";
                debugInfo.Add($"[PERF] RAM Available = {availGb:F1} GB");
            }
            catch (Exception ex) { ErrorLog.Write("PerfCounter.Ram", ex); }
        }

        if (!data.Values.ContainsKey(SType.RamUsage) &&
            data.GetValue(SType.RamTotal) > 0 && data.GetValue(SType.RamAvailable) > 0)
        {
            var total = data.GetValue(SType.RamTotal);
            data.Values[SType.RamUsage] = Math.Clamp((total - data.GetValue(SType.RamAvailable)) / total * 100.0, 0, 100);
            data.Labels[SType.RamUsage] = "RAM Uso (calculado)";
        }
    }

    private static double ReadCpuTempFromThermalZone()
    {
        using var searcher = new ManagementObjectSearcher(
            @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
        using var results = searcher.Get(); // coleção COM do WMI também precisa ser descartada
        foreach (var obj in results)
        {
            using (obj)
            {
                var temp = Convert.ToDouble(obj["CurrentTemperature"] ?? 0);
                if (temp > 0)
                    return (temp - 2731.5) / 10.0;
            }
        }
        return 0;
    }

    private static double ReadCpuTempFromPerfCounter()
    {
        // Temperatura em Kelvin (inteiro) nesta classe de desempenho.
        using var searcher = new ManagementObjectSearcher(
            "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
        using var results = searcher.Get(); // coleção COM do WMI também precisa ser descartada
        foreach (var obj in results)
        {
            using (obj)
            {
                var kelvin = Convert.ToDouble(obj["Temperature"] ?? 0);
                if (kelvin > 0)
                    return kelvin - 273.15;
            }
        }
        return 0;
    }

    private static (double availableGB, double totalGB) ReadRamFromWmi()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem");
        using var results = searcher.Get(); // coleção COM do WMI também precisa ser descartada
        foreach (var obj in results)
        {
            using (obj)
            {
                // Valores em KB
                var free = Convert.ToDouble(obj["FreePhysicalMemory"] ?? 0);
                var total = Convert.ToDouble(obj["TotalVisibleMemorySize"] ?? 0);
                return (free / 1024.0 / 1024.0, total / 1024.0 / 1024.0);
            }
        }
        return (0, 0);
    }

    // ---------- Mapeamento de sensores ----------

    private static void Put(SensorData data, SType type, double val, string label)
    {
        data.Values[type] = val;
        data.Labels[type] = label;
    }

    /// <summary>Mantém o maior valor entre vários sensores do mesmo tipo (ex.: vários núcleos/discos).</summary>
    private static void PutMax(SensorData data, SType type, double val, string label)
    {
        if (!data.Values.TryGetValue(type, out var old) || val > old)
            Put(data, type, val, label);
    }

    /// <summary>Soma valores de vários sensores (ex.: throughput de vários discos/adaptadores).</summary>
    private static void PutSum(SensorData data, SType type, double val, string label)
    {
        data.Values.TryGetValue(type, out var old);
        Put(data, type, old + val, label);
    }

    /// <summary>Só substitui o valor atual se este sensor tiver prioridade maior ou igual.</summary>
    private void PutPreferred(SensorData data, SType type, double val, string label, int priority)
    {
        if (_priority.TryGetValue(type, out var current) && priority < current) return;
        _priority[type] = priority;
        Put(data, type, val, label);
    }

    private void MapSensor(ISensor sensor, SensorData data)
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
            case HardwareType.EmbeddedController:
                MapMotherboardSensor(sensor, v, label, data);
                break;
        }
    }

    private static int CpuTempPriority(string name)
    {
        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Average", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (name.Contains("Max", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    private void MapCpuSensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Load when sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.CpuTotal, val, label);
                break;
            case LhmSensorType.Load:
                PutMax(data, SType.CpuPerCore, val, label);
                break;
            case LhmSensorType.Temperature:
                PutPreferred(data, SType.CpuTemperature, val, label, CpuTempPriority(sensor.Name));
                break;
            case LhmSensorType.Clock:
                PutMax(data, SType.CpuClock, val, label);
                break;
            case LhmSensorType.Voltage:
                Put(data, SType.CpuVoltage, val, label);
                break;
            case LhmSensorType.Power when sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase):
                PutPreferred(data, SType.CpuPower, val, label, 2);
                break;
            case LhmSensorType.Power:
                PutPreferred(data, SType.CpuPower, val, label, 1);
                break;
        }
    }

    private void MapGpuSensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Load when sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuCore, val, label);
                break;
            case LhmSensorType.Load when sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuMemory, val, label);
                break;
            // SmallData: VRAM real em MB (GPU Memory total/usada). "D3D Dedicated" serve de reserva
            // (GPUs integradas costumam expor só a variante D3D).
            case LhmSensorType.SmallData when sensor.Name.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase):
                PutPreferred(data, SType.GpuMemoryTotal, val, label, 2);
                break;
            case LhmSensorType.SmallData when sensor.Name.Contains("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase):
                PutPreferred(data, SType.GpuMemoryTotal, val, label, 1);
                break;
            case LhmSensorType.SmallData when sensor.Name.Contains("GPU Memory Used", StringComparison.OrdinalIgnoreCase):
                PutPreferred(data, SType.GpuMemoryUsed, val, label, 2);
                break;
            case LhmSensorType.SmallData when sensor.Name.Contains("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase):
                PutPreferred(data, SType.GpuMemoryUsed, val, label, 1);
                break;
            case LhmSensorType.Clock when sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuCoreClock, val, label);
                break;
            case LhmSensorType.Clock when sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuMemoryClock, val, label);
                break;
            case LhmSensorType.Temperature when sensor.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuHotSpot, val, label);
                break;
            case LhmSensorType.Temperature:
                // "GPU Core" tem prioridade sobre "Memory Junction", etc.
                PutPreferred(data, SType.GpuTemperature, val, label,
                    sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
                break;
            case LhmSensorType.Voltage:
                Put(data, SType.GpuVoltage, val, label);
                break;
            case LhmSensorType.Fan:
                Put(data, SType.GpuFan, val, label);
                break;
            case LhmSensorType.Power:
                Put(data, SType.GpuPower, val, label);
                break;
        }
    }

    private static void MapMemorySensor(ISensor sensor, double val, string label, SensorData data)
    {
        // Ignora a memória virtual (pagefile): só a RAM física interessa.
        if (sensor.Hardware.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
            return;

        switch (sensor.SensorType)
        {
            case LhmSensorType.Load:
                Put(data, SType.RamUsage, val, label);
                break;
            // LibreHardwareMonitor reporta sensores Data de memória em GB.
            case LhmSensorType.Data when sensor.Name.Contains("Available", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.RamAvailable, val, label);
                break;
            case LhmSensorType.Data when sensor.Name.Contains("Used", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.RamUsed, val, label);
                break;
        }
    }

    private static void MapStorageSensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            // "Total Activity" = % de uso do disco; "Used Space" (capacidade ocupada) é ignorado.
            case LhmSensorType.Load when sensor.Name.Contains("Activity", StringComparison.OrdinalIgnoreCase):
                PutMax(data, SType.DiskUsage, val, label);
                break;
            case LhmSensorType.Throughput when sensor.Name.Contains("Read", StringComparison.OrdinalIgnoreCase):
                PutSum(data, SType.DiskRead, val, label);
                break;
            case LhmSensorType.Throughput when sensor.Name.Contains("Write", StringComparison.OrdinalIgnoreCase):
                PutSum(data, SType.DiskWrite, val, label);
                break;
        }
    }

    private static void MapNetworkSensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Throughput when sensor.Name.Contains("Upload", StringComparison.OrdinalIgnoreCase):
                PutSum(data, SType.NetworkUp, val, label);
                break;
            case LhmSensorType.Throughput when sensor.Name.Contains("Download", StringComparison.OrdinalIgnoreCase):
                PutSum(data, SType.NetworkDown, val, label);
                break;
        }
    }

    private static void MapMotherboardSensor(ISensor sensor, double val, string label, SensorData data)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Temperature:
                PutMax(data, SType.MotherboardTemp, val, label);
                break;
            case LhmSensorType.Fan:
                PutMax(data, SType.FanSpeed, val, label);
                break;
        }
    }

    // ---------- Processos na GPU ----------

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

    public void Dispose()
    {
        if (_disposed) return;

        Stop();

        lock (_sync)
        {
            _disposed = true;
            _timer.Dispose();
            _fpsMonitor.Dispose();
            _ramCounter?.Dispose();
        }
    }
}

internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware) { hardware.Update(); foreach (var sh in hardware.SubHardware) sh.Accept(this); }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}
