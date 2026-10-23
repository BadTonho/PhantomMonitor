using System.Globalization;
using System.IO;
using System.Security;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services.Linux;

/// <summary>
/// Coleta sensores de GPU por dispositivo DRM, usando NVML, atributos DRM,
/// hwmon vinculado ao dispositivo e estatísticas DRM fdinfo quando disponíveis.
/// </summary>
internal sealed partial class LinuxGpuSensorReader : IDisposable
{
    private static readonly SensorType[] DiagnosticTypes =
    [
        SensorType.GpuCore,
        SensorType.GpuMemoryUsed,
        SensorType.GpuMemoryTotal,
        SensorType.GpuTemperature,
        SensorType.GpuHotSpot,
        SensorType.GpuVramTemperature,
        SensorType.GpuFan
    ];

    private static readonly SensorType[] AvailabilityWarningTypes =
    [
        SensorType.GpuCore,
        SensorType.GpuMemoryUsed,
        SensorType.GpuMemoryTotal,
        SensorType.GpuTemperature,
        SensorType.GpuHotSpot,
        SensorType.GpuVramTemperature
    ];

    private readonly string _drmRoot;
    private readonly string _hwmonRoot;
    private readonly string _thermalRoot;
    private readonly NvidiaGpuSensorReader _nvidiaReader;
    private readonly DrmFdInfoGpuUsageReader _fdInfoReader;
    private readonly Dictionary<(int CardIndex, SensorType Type), HashSet<string>> _sources = [];
    private readonly Dictionary<(int CardIndex, SensorType Type), HashSet<string>> _sourceIssues = [];
    private readonly Dictionary<int, string> _nvidiaStatuses = [];
    private readonly Dictionary<SensorType, HashSet<string>> _unassociatedHwmonSources = [];
    private List<GpuDevice> _devices = [];
    private Dictionary<string, HwmonAssociation> _hwmonAssociations = new(StringComparer.Ordinal);
    private List<ThermalZoneAssociation> _thermalZones = [];
    private IReadOnlyDictionary<string, double> _fdInfoUsage = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    public LinuxGpuSensorReader(
        string drmRoot = "/sys/class/drm",
        string hwmonRoot = "/sys/class/hwmon",
        string procRoot = "/proc",
        NvidiaGpuSensorReader? nvidiaReader = null,
        DrmFdInfoGpuUsageReader? fdInfoReader = null,
        string drmDeviceRoot = "/dev/dri",
        string thermalRoot = "/sys/class/thermal")
    {
        _drmRoot = drmRoot;
        _hwmonRoot = hwmonRoot;
        _thermalRoot = thermalRoot;
        _nvidiaReader = nvidiaReader ?? new NvidiaGpuSensorReader();
        _fdInfoReader = fdInfoReader ?? new DrmFdInfoGpuUsageReader(procRoot, drmDeviceRoot: drmDeviceRoot);
    }

    public void BeginSample(DateTime timestampUtc)
    {
        _sources.Clear();
        _sourceIssues.Clear();
        _nvidiaStatuses.Clear();
        _unassociatedHwmonSources.Clear();
        _devices = EnumerateDevices();
        _hwmonAssociations = EnumerateHwmonAssociations();
        _thermalZones = EnumerateThermalZones();
        _fdInfoUsage = _fdInfoReader.Sample(
            timestampUtc,
            _devices.Select(device => device.PciBusId).Where(id => id is not null).Cast<string>().ToArray());
    }

    public bool IsGpuHwmonDirectory(string directory, out int cardIndex, out string label)
    {
        if (_hwmonAssociations.TryGetValue(directory, out var association))
        {
            cardIndex = association.Device?.Index ?? 0;
            label = association.Device?.Label ?? association.Name;
            return true;
        }

        cardIndex = 0;
        label = "";
        return false;
    }

    public void ReadHwmonTemperature(
        SensorData data,
        string hwmonDirectory,
        int cardIndex,
        string hwmonName,
        string label,
        string tempFile,
        int fileSlot)
    {
        var channelLabel = TryReadText(Path.Combine(hwmonDirectory, $"temp{fileSlot}_label"), out var rawLabel)
            ? rawLabel.ToLowerInvariant()
            : "";

        // Esses sensores pertencem a estágios de alimentação/placa, não ao diodo da GPU.
        if (channelLabel.Contains("vrm", StringComparison.Ordinal) ||
            channelLabel.Contains("liquid", StringComparison.Ordinal) ||
            channelLabel.Contains("plx", StringComparison.Ordinal))
            return;

        var type = channelLabel.Contains("junction", StringComparison.Ordinal) ||
                   channelLabel.Contains("hotspot", StringComparison.Ordinal)
            ? SensorType.GpuHotSpot
            : channelLabel.Contains("mem", StringComparison.Ordinal) ||
              channelLabel.Contains("vram", StringComparison.Ordinal)
                ? SensorType.GpuVramTemperature
                : SensorType.GpuTemperature;

        var status = ReadNumericFile(tempFile, out var milliCelsius);
        if (status != NumericFileStatus.Valid)
        {
            RecordSourceIssue(cardIndex, type, DescribeFileIssue("hwmon", tempFile, status));
            return;
        }

        if (milliCelsius is < 0 or > 200_000)
        {
            RecordSourceIssue(cardIndex, type,
                $"hwmon/{Path.GetFileName(tempFile)} publicou valor fora do intervalo térmico esperado");
            return;
        }

        var source = $"hwmon({hwmonName})/{Path.GetFileName(tempFile)}";
        PublishTemperature(data, type, cardIndex, milliCelsius / 1000d, label, source);
    }

    public void ReportHwmonDirectoryIssue(int cardIndex, string directory, string status)
    {
        var issue = $"hwmon/{Path.GetFileName(directory)} não pôde enumerar canais de temperatura ({status})";
        RecordSourceIssue(cardIndex, SensorType.GpuTemperature, issue);
        RecordSourceIssue(cardIndex, SensorType.GpuHotSpot, issue);
        RecordSourceIssue(cardIndex, SensorType.GpuVramTemperature, issue);
    }

    public void PublishGpuFan(SensorData data, int cardIndex, double rpm, string label, string source)
    {
        if (!double.IsFinite(rpm) || rpm < 0) return;
        PublishMetric(data, SensorType.GpuFan, cardIndex, rpm, label, source, takeMaximum: true);
    }

    public void ReadDeviceMetrics(SensorData data)
    {
        var nvidiaIndex = 0u;
        foreach (var device in _devices)
        {
            if (device.Vendor.Equals("0x10de", StringComparison.OrdinalIgnoreCase))
            {
                if (_nvidiaReader.TryRead(device.PciBusId, nvidiaIndex, out var reading))
                {
                    _nvidiaStatuses[device.Index] = _nvidiaReader.Status;
                    if (reading.UtilizationPercent is double utilization)
                        PublishMetric(data, SensorType.GpuCore, device.Index, utilization, reading.Label, "NVML", onlyWhenMissing: true);
                    if (reading.MemoryUsedMb is double used)
                        PublishMetric(data, SensorType.GpuMemoryUsed, device.Index, used, reading.Label, "NVML", onlyWhenMissing: true);
                    if (reading.MemoryTotalMb is double total)
                        PublishMetric(data, SensorType.GpuMemoryTotal, device.Index, total, reading.Label, "NVML", onlyWhenMissing: true);
                    if (reading.TemperatureCelsius is double temperature)
                        PublishTemperature(data, SensorType.GpuTemperature, device.Index, temperature, reading.Label, "NVML", onlyWhenMissing: true);
                }
                else
                {
                    _nvidiaStatuses[device.Index] = _nvidiaReader.Status;
                }
                nvidiaIndex++;
            }

            ReadDrmAttribute(data, device, "gpu_busy_percent", SensorType.GpuCore, value => value, "DRM/gpu_busy_percent");
            ReadDrmAttribute(data, device, "mem_info_vram_used", SensorType.GpuMemoryUsed,
                bytes => bytes / (1024d * 1024d), "DRM/mem_info_vram_used");
            ReadDrmAttribute(data, device, "mem_info_vram_total", SensorType.GpuMemoryTotal,
                bytes => bytes / (1024d * 1024d), "DRM/mem_info_vram_total");
        }

        foreach (var (pciBusId, usage) in _fdInfoUsage)
        {
            var device = _devices.FirstOrDefault(candidate =>
                candidate.PciBusId is not null && NormalizePciBusId(candidate.PciBusId) == pciBusId);
            if (device is null) continue;

            PublishMetric(data, SensorType.GpuCore, device.Index, usage, device.Label,
                "DRM fdinfo (mecanismo mais ocupado)", onlyWhenMissing: true);
        }

        ReadThermalZoneMetrics(data);
    }

    public void AppendDiagnostics(ICollection<string> lines, SensorData data)
    {
        data.SensorAvailabilityDiagnostics = [];
        if (_devices.Count == 0)
        {
            lines.Add("[GPU] Nenhuma placa gráfica foi detectada em /sys/class/drm/card*/device.");
            data.SensorAvailabilityDiagnostics.Add(new SensorAvailabilityDiagnostic(
                "Sistema", null, false, "DRM (/sys/class/drm)",
                "Nenhuma placa DRM foi enumerada pelo coletor."));
            foreach (var type in DiagnosticTypes)
                lines.Add($"[GPU] {type}: indisponível — nenhum dispositivo DRM foi detectado.");
            return;
        }

        foreach (var device in _devices)
        {
            lines.Add($"[GPU] DRM {device.CardName}: vendor={device.Vendor}; driver={device.Driver}; PCI={device.PciBusId ?? "não identificado"}.");
            if (device.Vendor.Equals("0x10de", StringComparison.OrdinalIgnoreCase))
            {
                var status = _nvidiaStatuses.GetValueOrDefault(device.Index, _nvidiaReader.Status);
                lines.Add($"[GPU] {device.CardName} NVML: {status}");
            }

            foreach (var type in DiagnosticTypes)
            {
                var sources = GetSources(device.Index, type);
                if (sources.Length > 0)
                {
                    lines.Add($"[GPU] {device.CardName} {type}: disponível via {string.Join(", ", sources)}.");
                    continue;
                }

                lines.Add($"[GPU] {device.CardName} {type}: indisponível — {MissingReason(device, type)}.");
            }

            foreach (var type in AvailabilityWarningTypes)
            {
                var sources = GetSources(device.Index, type);
                var available = sources.Length > 0;
                data.SensorAvailabilityDiagnostics.Add(new SensorAvailabilityDiagnostic(
                    $"{device.CardName} · {device.Label}",
                    type,
                    available,
                    available ? string.Join(", ", sources) : ExpectedSources(device, type),
                    available ? "" : MissingReason(device, type)));
            }
        }

        foreach (var (type, sources) in _unassociatedHwmonSources)
            lines.Add($"[GPU] {type}: leitura canônica por {string.Join(", ", sources)}; hwmon sem associação PCI.");

        lines.Add($"[GPU] DRM fdinfo: {_fdInfoReader.Status}");
    }

    private void ReadThermalZoneMetrics(SensorData data)
    {
        foreach (var zone in _thermalZones)
        {
            var path = Path.Combine(zone.Directory, "temp");
            var status = ReadNumericFile(path, out var milliCelsius);
            if (status != NumericFileStatus.Valid)
            {
                if (status != NumericFileStatus.Missing)
                    RecordSourceIssue(zone.Device.Index, SensorType.GpuTemperature,
                        DescribeFileIssue($"thermal_zone({zone.Type})", path, status));
                continue;
            }

            if (milliCelsius is < 0 or > 200_000)
            {
                RecordSourceIssue(zone.Device.Index, SensorType.GpuTemperature,
                    $"thermal_zone({zone.Type})/{Path.GetFileName(path)} publicou valor fora do intervalo térmico esperado");
                continue;
            }

            PublishTemperature(data, SensorType.GpuTemperature, zone.Device.Index,
                milliCelsius / 1000d, zone.Device.Label, $"thermal_zone({zone.Type})/temp", onlyWhenMissing: true);
        }
    }

    private void ReadDrmAttribute(
        SensorData data,
        GpuDevice device,
        string fileName,
        SensorType type,
        Func<double, double> convert,
        string source)
    {
        var path = Path.Combine(device.DevicePath, fileName);
        var status = ReadNumericFile(path, out var rawValue);
        if (status != NumericFileStatus.Valid)
        {
            if (status is NumericFileStatus.Inaccessible or NumericFileStatus.Invalid)
                RecordSourceIssue(device.Index, type, DescribeFileIssue(source, path, status));
            return;
        }
        if (type == SensorType.GpuCore && rawValue is < 0 or > 100)
        {
            RecordSourceIssue(device.Index, type, $"{source} publicou valor fora do intervalo de 0 a 100%");
            return;
        }
        if (type != SensorType.GpuCore && rawValue < 0)
        {
            RecordSourceIssue(device.Index, type, $"{source} publicou valor negativo");
            return;
        }

        var value = convert(rawValue);
        if (!double.IsFinite(value))
        {
            RecordSourceIssue(device.Index, type, $"{source} publicou um valor não finito");
            return;
        }
        PublishMetric(data, type, device.Index, value, device.Label, $"{device.CardName}/{source}", onlyWhenMissing: true);
    }

    private void PublishTemperature(
        SensorData data,
        SensorType type,
        int cardIndex,
        double value,
        string label,
        string source,
        bool onlyWhenMissing = false)
    {
        if (!double.IsFinite(value) || value is < 0 or > 200) return;
        PublishMetric(data, type, cardIndex, value, label, source, takeMaximum: true, onlyWhenMissing);
    }

    private void PublishMetric(
        SensorData data,
        SensorType type,
        int cardIndex,
        double value,
        string label,
        string source,
        bool takeMaximum = false,
        bool onlyWhenMissing = false)
    {
        if (!double.IsFinite(value)) return;

        if (cardIndex > 0)
        {
            var key = new SensorKey(type, cardIndex);
            var hasInstance = data.Series.TryGetValue(key, out var currentInstance);
            if (onlyWhenMissing && hasInstance) return;
            if (!hasInstance || !takeMaximum || value > currentInstance)
                data.SetValue(key, value, label);
        }

        var hasCanonical = data.Values.TryGetValue(type, out var currentCanonical);
        if (!hasCanonical || (takeMaximum && value > currentCanonical))
            data.SetValue(type, value, label);

        AddSource(cardIndex, type, source);
    }

    private void AddSource(int cardIndex, SensorType type, string source)
    {
        if (cardIndex <= 0)
        {
            if (!_unassociatedHwmonSources.TryGetValue(type, out var unassociated))
                _unassociatedHwmonSources[type] = unassociated = new HashSet<string>(StringComparer.Ordinal);
            unassociated.Add(source);
            return;
        }

        if (!_sources.TryGetValue((cardIndex, type), out var sources))
            _sources[(cardIndex, type)] = sources = new HashSet<string>(StringComparer.Ordinal);
        sources.Add(source);
    }

    private string[] GetSources(int cardIndex, SensorType type) =>
        _sources.TryGetValue((cardIndex, type), out var sources)
            ? sources.OrderBy(source => source, StringComparer.Ordinal).ToArray()
            : [];

    private string MissingReason(GpuDevice device, SensorType type)
    {
        var driver = device.Driver;
        var isNvidia = device.Vendor.Equals("0x10de", StringComparison.OrdinalIgnoreCase);
        var baseReason = type switch
        {
            SensorType.GpuCore when driver is "i915" or "xe" =>
                $"o driver {driver} não publicou uma amostra de uso; fdinfo: {TrimTerminalPeriod(_fdInfoReader.GetDeviceStatus(device.PciBusId))}",
            SensorType.GpuCore when isNvidia =>
                $"NVML: {GetNvidiaStatus(device.Index)}; DRM/fdinfo: {TrimTerminalPeriod(_fdInfoReader.GetDeviceStatus(device.PciBusId))}",
            SensorType.GpuCore => $"gpu_busy_percent e drm-engine-* não publicaram amostra válida; fdinfo: {TrimTerminalPeriod(_fdInfoReader.GetDeviceStatus(device.PciBusId))}",
            SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal when isNvidia =>
                $"NVML: {GetNvidiaStatus(device.Index)}; contador DRM agregado de VRAM ausente",
            SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal when driver is "i915" or "xe" =>
                "o driver não publicou contador agregado de memória de vídeo; RAM compartilhada não é usada como substituta",
            SensorType.GpuMemoryUsed => "mem_info_vram_used ou NVML não publicou uso agregado de VRAM",
            SensorType.GpuMemoryTotal => "mem_info_vram_total ou NVML não publicou capacidade de VRAM",
            SensorType.GpuTemperature when isNvidia =>
                $"hwmon/thermal_zone não publicou temperatura e NVML: {GetNvidiaStatus(device.Index)}",
            SensorType.GpuTemperature => "hwmon/thermal_zone não publicou canal de temperatura associado à GPU",
            SensorType.GpuHotSpot => "hwmon não publicou canal com rótulo hotspot/junction",
            SensorType.GpuVramTemperature => "hwmon não publicou canal com rótulo mem/vram",
            SensorType.GpuFan => "hwmon não publicou fan*_input da GPU",
            _ => "nenhuma fonte compatível publicou esta métrica"
        };

        return _sourceIssues.TryGetValue((device.Index, type), out var issues) && issues.Count > 0
            ? $"{baseReason}; fonte encontrada, mas inválida/inacessível: {string.Join("; ", issues)}"
            : baseReason;
    }

    private static string ExpectedSources(GpuDevice device, SensorType type)
    {
        var isNvidia = device.Vendor.Equals("0x10de", StringComparison.OrdinalIgnoreCase);
        return type switch
        {
            SensorType.GpuCore when isNvidia => "NVML, DRM/gpu_busy_percent, DRM fdinfo",
            SensorType.GpuCore => "DRM/gpu_busy_percent, DRM fdinfo",
            SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal when isNvidia => "NVML, DRM/mem_info_vram_*",
            SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal => "DRM/mem_info_vram_*",
            SensorType.GpuTemperature when isNvidia => "NVML, hwmon, thermal_zone",
            SensorType.GpuTemperature => "hwmon, thermal_zone",
            SensorType.GpuHotSpot or SensorType.GpuVramTemperature => "hwmon",
            _ => "DRM/hwmon"
        };
    }

    private string GetNvidiaStatus(int cardIndex) =>
        TrimTerminalPeriod(_nvidiaStatuses.GetValueOrDefault(cardIndex, _nvidiaReader.Status));

    private static string TrimTerminalPeriod(string value) => value.TrimEnd('.');

    private static NumericFileStatus ReadNumericFile(string path, out double value)
    {
        value = 0;
        string raw;
        try
        {
            raw = File.ReadAllText(path).Trim();
        }
        catch (FileNotFoundException)
        {
            return NumericFileStatus.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return NumericFileStatus.Missing;
        }
        catch (Exception ex) when (IsExpectedSysfsFailure(ex))
        {
            return NumericFileStatus.Inaccessible;
        }

        return double.TryParse(raw.Split('\n')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value)
            ? NumericFileStatus.Valid
            : NumericFileStatus.Invalid;
    }

    private static string DescribeFileIssue(string source, string path, NumericFileStatus status) =>
        status switch
        {
            NumericFileStatus.Inaccessible => $"{source}/{Path.GetFileName(path)} foi localizado, mas não pôde ser lido (permissão ou remoção durante a amostra)",
            NumericFileStatus.Invalid => $"{source}/{Path.GetFileName(path)} contém valor inválido",
            _ => $"{source}/{Path.GetFileName(path)} desapareceu durante a amostra"
        };

    private void RecordSourceIssue(int cardIndex, SensorType type, string issue)
    {
        if (cardIndex <= 0) return;
        if (!_sourceIssues.TryGetValue((cardIndex, type), out var issues))
            _sourceIssues[(cardIndex, type)] = issues = new HashSet<string>(StringComparer.Ordinal);
        issues.Add(issue);
    }

    private static bool TryReadText(string path, out string text)
    {
        try
        {
            text = File.ReadAllText(path).Trim();
            return true;
        }
        catch (Exception ex) when (IsExpectedSysfsFailure(ex))
        {
            text = "";
            return false;
        }
    }

    private static bool IsExpectedSysfsFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;

    public void Dispose() => _nvidiaReader.Dispose();

    private sealed record GpuDevice(
        string CardName,
        int Index,
        string DevicePath,
        string? CanonicalDevicePath,
        string Vendor,
        string Driver,
        string? PciBusId,
        string Label);

    private sealed record HwmonAssociation(string Name, GpuDevice? Device);

    private sealed record ThermalZoneAssociation(string Directory, string Type, GpuDevice Device);

    private enum NumericFileStatus
    {
        Missing,
        Valid,
        Invalid,
        Inaccessible
    }
}
