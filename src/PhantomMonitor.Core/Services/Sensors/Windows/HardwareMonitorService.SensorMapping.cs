using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using PhantomMonitor.Models;
using LhmSensorType = LibreHardwareMonitor.Hardware.SensorType;
using SType = PhantomMonitor.Models.SensorType;

namespace PhantomMonitor.Services;

public sealed partial class HardwareMonitorService
{
    private void ProcessHardware(IHardware hardware, SensorData data, List<string> debugInfo, int depth, int gpuInstance = 0)
    {
        var indent = new string(' ', depth * 2);
        debugInfo.Add($"{indent}[HWR] {hardware.HardwareType}: {hardware.Name}");

        var category = hardware.HardwareType switch
        {
            HardwareType.Cpu => "cpu",
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "gpu",
            HardwareType.Motherboard => "motherboard",
            _ => null
        };
        if (category is not null && !string.IsNullOrWhiteSpace(hardware.Name))
            data.HardwareDevices.Add(new HardwareIdentity(category, hardware.Identifier.ToString(), hardware.Name));

        if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            gpuInstance = _hardwareInstanceFor.GetValueOrDefault(hardware, gpuInstance);

        // Instância "#i" quando o nó de topo for disco ou interface de rede
        // (GPUs já têm índice pré-computado: a dedicada é a GPU 1).
        if (depth == 0 && hardware.HardwareType == HardwareType.Storage)
            _hardwareInstanceFor[hardware] = ++_diskInstanceCursor;
        if (depth == 0 && hardware.HardwareType == HardwareType.Network)
            _hardwareInstanceFor[hardware] = ++_netInstanceCursor;

        foreach (var sensor in hardware.Sensors)
        {
            MapSensor(sensor, data, gpuInstance);
            var val = sensor.Value?.ToString("F2") ?? "null";
            debugInfo.Add($"{indent}  [{hardware.HardwareType}] {sensor.Name} ({sensor.SensorType}) = {val}");
        }

        // Sensores de placa-mãe/SuperIO ficam em sub-hardware.
        foreach (var sub in hardware.SubHardware)
        {
            ProcessHardware(sub, data, debugInfo, depth + 1, gpuInstance);
        }
    }

    private void AppendGpuAvailabilityDiagnostics(SensorData data, List<string> debugInfo)
    {
        data.SensorAvailabilityDiagnostics = [];
        var gpus = _computer.Hardware
            .Where(hardware => hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .ToArray();

        if (gpus.Length == 0)
        {
            const string reason = "O LibreHardwareMonitor não enumerou nenhum dispositivo GPU nesta amostra.";
            data.SensorAvailabilityDiagnostics.Add(new SensorAvailabilityDiagnostic(
                "Sistema", null, false, "LibreHardwareMonitor", reason));
            debugInfo.Add($"[GPU] {reason}");
            return;
        }

        foreach (var gpu in gpus)
        {
            var instance = _hardwareInstanceFor.GetValueOrDefault(gpu);
            var reported = _gpuMetricsReported.GetValueOrDefault(instance);
            foreach (var metric in GpuAvailabilityTypes)
            {
                var available = reported?.Contains(metric) == true;
                var reason = available
                    ? ""
                    : "O LibreHardwareMonitor não publicou uma leitura válida desta métrica nesta amostra.";
                data.SensorAvailabilityDiagnostics.Add(new SensorAvailabilityDiagnostic(
                    gpu.Name, metric, available, "LibreHardwareMonitor", reason));
                debugInfo.Add($"[GPU] {gpu.Name} {metric}: {(available ? "disponível via LibreHardwareMonitor" : $"indisponível — {reason}")}");
            }
        }
    }

    private void TrackGpuMetric(int instance, SType metric)
    {
        if (instance <= 0) return;
        if (!_gpuMetricsReported.TryGetValue(instance, out var reported))
            _gpuMetricsReported[instance] = reported = [];
        reported.Add(metric);
    }

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

    /// <summary>
    /// Série por instância ("Categoria#i", i ≥ 1): multi-GPU, núcleos, ventoinhas, discos...
    /// Não mexe na série canônica de índice 0 (compatibilidade com painel/alertas legados).
    /// </summary>
    private static void PutInstance(SensorData data, SType type, int instance, double val, string label)
    {
        if (instance <= 0) return;
        data.SetValue(new SensorKey(type, instance), val, label);
    }

    /// <summary>Slot estável por nome para séries que não têm número natural ("+12V", "Fan #2"...).</summary>
    private int SlotFor(string name)
    {
        if (!_seriesSlots.TryGetValue(name, out var slot))
            slot = _seriesSlots[name] = _seriesSlots.Count + 1;
        return slot;
    }

    /// <summary>Só substitui o valor atual se este sensor tiver prioridade maior ou igual.</summary>
    private void PutPreferred(SensorData data, SType type, double val, string label, int priority)
    {
        if (_priority.TryGetValue(type, out var current) && priority < current) return;
        _priority[type] = priority;
        Put(data, type, val, label);
    }

    private void MapSensor(ISensor sensor, SensorData data, int gpuDiagnosticInstance = 0)
    {
        var val = sensor.Value;
        if (!val.HasValue) return;

        double v = val.Value;
        var label = $"{sensor.Name} ({sensor.SensorType})";

        // Índice de instância do nó (GPU 1..n, disco 1..n, interface 1..n; 0 = sem série extra).
        var instance = _hardwareInstanceFor.GetValueOrDefault(sensor.Hardware);

        switch (sensor.Hardware.HardwareType)
        {
            case HardwareType.Cpu:
                MapCpuSensor(sensor, v, label, data);
                break;
            case HardwareType.GpuNvidia:
            case HardwareType.GpuAmd:
            case HardwareType.GpuIntel:
                MapGpuSensor(sensor, v, label, data, instance, sensor.Hardware.Name,
                    gpuDiagnosticInstance > 0 ? gpuDiagnosticInstance : instance);
                break;
            case HardwareType.Memory:
                MapMemorySensor(sensor, v, label, data);
                break;
            case HardwareType.Storage:
                MapStorageSensor(sensor, v, label, data, instance, sensor.Hardware.Name);
                break;
            case HardwareType.Network:
                MapNetworkSensor(sensor, v, label, data, instance, sensor.Hardware.Name);
                break;
            case HardwareType.Motherboard:
            case HardwareType.SuperIO:
            case HardwareType.EmbeddedController:
                MapMotherboardSensor(sensor, v, label, data, sensor.Hardware.Name);
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
        // Índice do núcleo no nome ("CPU Core #3", "Core #1 Max"); 0 = sensor global
        // (Package, Tctl/Tdie, Average, VCore...), que não vira série por núcleo.
        int core = ParseCoreIndex(sensor.Name);

        switch (sensor.SensorType)
        {
            case LhmSensorType.Load when sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.CpuTotal, val, label);
                break;
            case LhmSensorType.Load:
                PutMax(data, SType.CpuPerCore, val, label);
                PutInstance(data, SType.CpuPerCore, core, val, label);
                break;
            case LhmSensorType.Temperature:
                PutPreferred(data, SType.CpuTemperature, val, label, CpuTempPriority(sensor.Name));
                PutInstance(data, SType.CpuTemperature, core, val, label);
                break;
            case LhmSensorType.Clock:
                if (!double.IsFinite(val) || val <= 0) break;
                PutMax(data, SType.CpuClock, val, label);
                PutInstance(data, SType.CpuClock, core, val, label);
                break;
            case LhmSensorType.Voltage:
                Put(data, SType.CpuVoltage, val, label);
                break;
            case LhmSensorType.Power when sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase):
                PutPreferred(data, SType.CpuPower, val, label, 2);
                break;
            case LhmSensorType.Power:
                PutPreferred(data, SType.CpuPower, val, label, 1);
                PutInstance(data, SType.CpuPower, SlotFor(sensor.Name), val, label);
                break;
        }
    }

    /// <summary>Número do núcleo num nome de sensor ("CPU Core #2" → 2; nomes globais → 0).</summary>
    private static int ParseCoreIndex(string name)
    {
        var match = CoreIndexRegex.Match(name);
        return match.Success && int.TryParse(match.Groups[1].Value, out var idx) && idx > 0 ? idx : 0;
    }

    private static readonly Regex CoreIndexRegex = new(@"#(\d+)", RegexOptions.Compiled);

    private void MapGpuSensor(
        ISensor sensor,
        double val,
        string label,
        SensorData data,
        int instance,
        string gpuName,
        int diagnosticInstance)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Load when sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuCore, val, label);
                PutInstance(data, SType.GpuCore, instance, val, gpuName);
                TrackGpuMetric(diagnosticInstance, SType.GpuCore);
                break;
            case LhmSensorType.Load when sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuMemory, val, label);
                PutInstance(data, SType.GpuMemory, instance, val, gpuName);
                break;
            // SmallData: VRAM real em MB (GPU Memory total/usada). "D3D Dedicated" serve de reserva
            // (GPUs integradas costumam expor só a variante D3D).
            case LhmSensorType.SmallData when sensor.Name.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase):
                PutPreferred(data, SType.GpuMemoryTotal, val, label, 2);
                PutInstance(data, SType.GpuMemoryTotal, instance, val, gpuName);
                TrackGpuMetric(diagnosticInstance, SType.GpuMemoryTotal);
                break;
            case LhmSensorType.SmallData when sensor.Name.Contains("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase):
                PutPreferred(data, SType.GpuMemoryTotal, val, label, 1);
                PutInstance(data, SType.GpuMemoryTotal, instance, val, gpuName);
                TrackGpuMetric(diagnosticInstance, SType.GpuMemoryTotal);
                break;
            case LhmSensorType.SmallData when sensor.Name.Contains("GPU Memory Used", StringComparison.OrdinalIgnoreCase):
                PutPreferred(data, SType.GpuMemoryUsed, val, label, 2);
                PutInstance(data, SType.GpuMemoryUsed, instance, val, gpuName);
                TrackGpuMetric(diagnosticInstance, SType.GpuMemoryUsed);
                break;
            case LhmSensorType.SmallData when sensor.Name.Contains("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase):
                PutPreferred(data, SType.GpuMemoryUsed, val, label, 1);
                PutInstance(data, SType.GpuMemoryUsed, instance, val, gpuName);
                TrackGpuMetric(diagnosticInstance, SType.GpuMemoryUsed);
                break;
            case LhmSensorType.Clock when sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuCoreClock, val, label);
                PutInstance(data, SType.GpuCoreClock, instance, val, gpuName);
                break;
            case LhmSensorType.Clock when sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuMemoryClock, val, label);
                PutInstance(data, SType.GpuMemoryClock, instance, val, gpuName);
                break;
            case LhmSensorType.Temperature when sensor.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuHotSpot, val, label);
                PutInstance(data, SType.GpuHotSpot, instance, val, gpuName);
                TrackGpuMetric(diagnosticInstance, SType.GpuHotSpot);
                break;
            // Temperatura da junction da VRAM: NVIDIA "GPU Memory Temperature", AMD "GPU VRAM"
            // e nome legado "Memory Junction". "GPU VRM Memory" (fase do VRM, não a memória) fica
            // de fora do match pelo guard de "VRM"; antes caiam em GpuTemperature e sobrescreviam
            // a instância por GPU.
            case LhmSensorType.Temperature when
                (sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
                 sensor.Name.Contains("VRAM", StringComparison.OrdinalIgnoreCase)) &&
                !sensor.Name.Contains("VRM", StringComparison.OrdinalIgnoreCase):
                Put(data, SType.GpuVramTemperature, val, label);
                PutInstance(data, SType.GpuVramTemperature, instance, val, gpuName);
                TrackGpuMetric(diagnosticInstance, SType.GpuVramTemperature);
                break;
            // Fallthrough do restante: "GPU Core" tem prioridade sobre os demais diodos da GPU.
            // VRM/Liquid/PLX da AMD não são temperatura de diodo da GPU — não mapeiam nenhuma
            // série ainda (podiam despintar GpuTemperature#i com valor de fase do VRM).
            case LhmSensorType.Temperature when
                !sensor.Name.Contains("VRM", StringComparison.OrdinalIgnoreCase) &&
                !sensor.Name.Contains("Liquid", StringComparison.OrdinalIgnoreCase) &&
                !sensor.Name.Contains("PLX", StringComparison.OrdinalIgnoreCase):
                // "GPU Core" tem prioridade sobre "Memory Junction", etc.
                PutPreferred(data, SType.GpuTemperature, val, label,
                    sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
                PutInstance(data, SType.GpuTemperature, instance, val, gpuName);
                TrackGpuMetric(diagnosticInstance, SType.GpuTemperature);
                break;
            case LhmSensorType.Voltage:
                Put(data, SType.GpuVoltage, val, label);
                PutInstance(data, SType.GpuVoltage, instance, val, gpuName);
                break;
            case LhmSensorType.Fan:
                Put(data, SType.GpuFan, val, label);
                PutInstance(data, SType.GpuFan, instance, val, gpuName);
                break;
            case LhmSensorType.Power:
                Put(data, SType.GpuPower, val, label);
                PutInstance(data, SType.GpuPower, instance, val, gpuName);
                break;
        }
    }

    private void MapMemorySensor(ISensor sensor, double val, string label, SensorData data)
    {
        if (sensor.Hardware.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
            || sensor.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
        {
            if (sensor.SensorType == LhmSensorType.Data && sensor.Name.Contains("Used", StringComparison.OrdinalIgnoreCase) && val > 0)
                Put(data, SType.RamCommitted, val, "Memória comprometida");
            return;
        }

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
            case LhmSensorType.Clock when val > 0:
                PutPreferred(data, SType.RamFrequency, val < 1800 ? val * 2 : val, label, 2);
                break;
            case var _ when (sensor.Name.Contains("tCL", StringComparison.OrdinalIgnoreCase)
                || sensor.Name.Contains("CAS#", StringComparison.OrdinalIgnoreCase)) && val > 0:
                Put(data, SType.RamCasLatency, val, label);
                break;
            case var _ when sensor.Name.Contains("tRCD", StringComparison.OrdinalIgnoreCase) && val > 0:
                Put(data, SType.RamTrcd, val, label);
                break;
            case var _ when sensor.Name.Contains("tRP", StringComparison.OrdinalIgnoreCase) && val > 0:
                Put(data, SType.RamTrp, val, label);
                break;
            case var _ when sensor.Name.Contains("tRAS", StringComparison.OrdinalIgnoreCase) && val > 0:
                Put(data, SType.RamTras, val, label);
                break;
            case LhmSensorType.Temperature:
                // Temperatura de módulos de RAM (poucas placas/EC expõem; ex.: "RAM Temperature").
                data.SetValue(new SensorKey(SType.RamTemperature, SlotFor(sensor.Hardware.Name)), val, sensor.Hardware.Name);
                break;
        }
    }

    private static void MapStorageSensor(ISensor sensor, double val, string label, SensorData data, int instance, string diskName)
    {
        switch (sensor.SensorType)
        {
            // "Total Activity" = % de uso do disco; "Used Space" (capacidade ocupada) é ignorado.
            case LhmSensorType.Load when sensor.Name.Contains("Activity", StringComparison.OrdinalIgnoreCase):
                PutMax(data, SType.DiskUsage, val, label);
                PutInstance(data, SType.DiskUsage, instance, val, diskName);
                break;
            case LhmSensorType.Load when sensor.Name.Contains("Remaining Life", StringComparison.OrdinalIgnoreCase):
                // Saúde do SSD/pendrive em % sobrevivência (100 = novo).
                PutMax(data, SType.DiskHealth, val, label);
                PutInstance(data, SType.DiskHealth, instance, val, diskName);
                break;
            case LhmSensorType.Throughput when sensor.Name.Contains("Read", StringComparison.OrdinalIgnoreCase):
                PutSum(data, SType.DiskRead, val, label);
                PutInstance(data, SType.DiskRead, instance, val, diskName);
                break;
            case LhmSensorType.Throughput when sensor.Name.Contains("Write", StringComparison.OrdinalIgnoreCase):
                PutSum(data, SType.DiskWrite, val, label);
                PutInstance(data, SType.DiskWrite, instance, val, diskName);
                break;
            case LhmSensorType.Temperature:
                // NVMe/HD reportam "Temperature"; alguns SSDs também expõem "Temperature Ambient".
                if (sensor.Name.Contains("Ambient", StringComparison.OrdinalIgnoreCase)) break;
                PutMax(data, SType.DiskTemperature, val, label);
                PutInstance(data, SType.DiskTemperature, instance, val, diskName);
                break;
            case LhmSensorType.Data when sensor.Name.Contains("Total Host Read", StringComparison.OrdinalIgnoreCase):
                PutInstance(data, SType.DiskTotalRead, instance, val, diskName);
                break;
            case LhmSensorType.Data when sensor.Name.Contains("Total Host Write", StringComparison.OrdinalIgnoreCase):
                // Endurance do SSD (escritas acumuladas de fábrica, em GB).
                PutInstance(data, SType.DiskTotalWritten, instance, val, diskName);
                PutMax(data, SType.DiskTotalWritten, val, label);
                break;
            case LhmSensorType.Data when sensor.Name.Contains("Data Read", StringComparison.OrdinalIgnoreCase) && !sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase):
                PutInstance(data, SType.DiskTotalRead, instance, val, diskName);
                PutMax(data, SType.DiskTotalRead, val, label);
                break;
            case LhmSensorType.Data when sensor.Name.Contains("Data Written", StringComparison.OrdinalIgnoreCase):
                PutInstance(data, SType.DiskTotalWritten, instance, val, diskName);
                PutMax(data, SType.DiskTotalWritten, val, label);
                break;
        }
    }

    private static void MapNetworkSensor(ISensor sensor, double val, string label, SensorData data, int instance, string nicName)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Throughput when sensor.Name.Contains("Upload", StringComparison.OrdinalIgnoreCase):
                PutSum(data, SType.NetworkUp, val, label);
                PutInstance(data, SType.NetworkUp, instance, val, nicName);
                break;
            case LhmSensorType.Throughput when sensor.Name.Contains("Download", StringComparison.OrdinalIgnoreCase):
                PutSum(data, SType.NetworkDown, val, label);
                PutInstance(data, SType.NetworkDown, instance, val, nicName);
                break;
        }
    }

    private void MapMotherboardSensor(ISensor sensor, double val, string label, SensorData data, string boardName)
    {
        switch (sensor.SensorType)
        {
            case LhmSensorType.Temperature:
                PutMax(data, SType.MotherboardTemp, val, label);
                var kind = IsVrmSensor(sensor.Name)
                    ? SType.VrmTemperature
                    : SType.MotherboardTemp;
                data.SetValue(
                    new SensorKey(kind, SlotFor($"{boardName}\\{sensor.Name}")),
                    val,
                    $"{boardName} — {sensor.Name}");
                break;
            case LhmSensorType.Voltage:
                // Rails da placa-mãe (+12V, +5V, VCore...): só instância, sem série canônica.
                data.SetValue(
                    new SensorKey(SType.RailVoltage, SlotFor($"{boardName}\\{sensor.Name}")),
                    val,
                    $"{boardName} — {sensor.Name}");
                break;
            case LhmSensorType.Fan:
                PutMax(data, SType.FanSpeed, val, label);
                PutInstance(data, SType.FanSpeed, SlotFor($"{boardName}\\{sensor.Name}"), val, $"{boardName} — {sensor.Name}");
                break;
        }
    }

    /// <summary>Reconhece sensores de zona VRM/VCore (o rótulo vai para a série VrmTemperature).</summary>
    private static bool IsVrmSensor(string name) =>
        name.Contains("VRM", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("VR ", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("VSoC", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("VCore", StringComparison.OrdinalIgnoreCase);
}

