using System.Globalization;
using System.IO;
using System.Security;
using PhantomMonitor.Models;
using GpuDevice = PhantomMonitor.Services.Linux.LinuxGpuSensorReader.GpuDevice;
using HwmonAssociation = PhantomMonitor.Services.Linux.LinuxGpuSensorReader.HwmonAssociation;
using ThermalZoneAssociation = PhantomMonitor.Services.Linux.LinuxGpuSensorReader.ThermalZoneAssociation;

namespace PhantomMonitor.Services.Linux;

internal sealed class LinuxGpuDeviceDiscovery
{
    private readonly string _drmRoot;
    private readonly string _hwmonRoot;
    private readonly string _thermalRoot;
    private readonly Action<int, SensorType, string> _recordSourceIssue;
    private List<GpuDevice> _devices = [];

    public LinuxGpuDeviceDiscovery(
        string drmRoot,
        string hwmonRoot,
        string thermalRoot,
        Action<int, SensorType, string> recordSourceIssue)
    {
        _drmRoot = drmRoot;
        _hwmonRoot = hwmonRoot;
        _thermalRoot = thermalRoot;
        _recordSourceIssue = recordSourceIssue;
    }

    public DiscoveryResult Discover()
    {
        _devices = EnumerateDevices();
        return new DiscoveryResult(_devices, EnumerateHwmonAssociations(), EnumerateThermalZones());
    }

    public sealed record DiscoveryResult(
        List<GpuDevice> Devices,
        Dictionary<string, HwmonAssociation> HwmonAssociations,
        List<ThermalZoneAssociation> ThermalZones);
    private List<GpuDevice> EnumerateDevices()
    {
        var devices = new List<GpuDevice>();
        try
        {
            if (!Directory.Exists(_drmRoot)) return devices;
            var cardPaths = Directory.GetDirectories(_drmRoot)
                .Where(path => IsCardDirectory(Path.GetFileName(path)))
                .OrderBy(path => ParseCardNumber(Path.GetFileName(path)))
                .ToArray();

            for (var offset = 0; offset < cardPaths.Length; offset++)
            {
                var cardName = Path.GetFileName(cardPaths[offset]);
                var devicePath = Path.Combine(cardPaths[offset], "device");
                var vendor = LinuxGpuSensorReader.TryReadText(Path.Combine(devicePath, "vendor"), out var vendorText) ? vendorText : "não identificado";
                var driver = GetDriverName(devicePath) ?? "não identificado";
                var pciBusId = ReadPciBusId(devicePath);
                var label = BuildDeviceLabel(vendor, driver);
                devices.Add(new GpuDevice(
                    cardName,
                    offset + 1,
                    devicePath,
                    ResolvePath(devicePath),
                    vendor,
                    driver,
                    pciBusId,
                    label));
            }
        }
        catch (Exception ex) when (LinuxGpuSensorReader.IsExpectedSysfsFailure(ex))
        {
            ErrorLog.Write("LinuxGpuSensorReader.Enumerate", ex);
        }

        return devices;
    }

    private Dictionary<string, HwmonAssociation> EnumerateHwmonAssociations()
    {
        var result = new Dictionary<string, HwmonAssociation>(StringComparer.Ordinal);
        try
        {
            if (!Directory.Exists(_hwmonRoot)) return result;
            foreach (var directory in Directory.GetDirectories(_hwmonRoot))
            {
                var name = LinuxGpuSensorReader.TryReadText(Path.Combine(directory, "name"), out var rawName) ? rawName : "hwmon";
                var device = FindGpuForHwmon(directory, name);
                if (device is not null || IsGpuHwmonName(name))
                    result[directory] = new HwmonAssociation(name, device);
            }
        }
        catch (Exception ex) when (LinuxGpuSensorReader.IsExpectedSysfsFailure(ex))
        {
            ErrorLog.Write("LinuxGpuSensorReader.HwmonEnumeration", ex);
        }

        return result;
    }

    private GpuDevice? FindGpuForHwmon(string hwmonDirectory, string name)
    {
        var hwmonPath = ResolvePath(hwmonDirectory);
        var hwmonDevicePath = ResolvePath(Path.Combine(hwmonDirectory, "device"));

        foreach (var device in _devices)
        {
            if (device.CanonicalDevicePath is not null &&
                (IsPathWithin(hwmonPath, device.CanonicalDevicePath) ||
                 string.Equals(hwmonDevicePath, device.CanonicalDevicePath, StringComparison.Ordinal)))
                return device;
        }

        var matchingDrivers = _devices.Where(device => HwmonNameMatchesDriver(name, device.Driver)).ToArray();
        return matchingDrivers.Length == 1 ? matchingDrivers[0] : null;
    }

    private List<ThermalZoneAssociation> EnumerateThermalZones()
    {
        var result = new List<ThermalZoneAssociation>();
        try
        {
            if (!Directory.Exists(_thermalRoot)) return result;
            foreach (var directory in Directory.GetDirectories(_thermalRoot))
            {
                if (!Path.GetFileName(directory).StartsWith("thermal_zone", StringComparison.Ordinal)) continue;
                var type = LinuxGpuSensorReader.TryReadText(Path.Combine(directory, "type"), out var rawType) ? rawType : "";
                var zonePath = ResolvePath(directory);
                var linkedDevicePath = ResolvePath(Path.Combine(directory, "device"));
                var device = FindGpuForThermalZone(zonePath, linkedDevicePath, type);
                if (device is not null)
                    result.Add(new ThermalZoneAssociation(directory, type, device));
            }
        }
        catch (Exception ex) when (LinuxGpuSensorReader.IsExpectedSysfsFailure(ex))
        {
            foreach (var device in _devices)
                _recordSourceIssue(device.Index, SensorType.GpuTemperature,
                    $"/sys/class/thermal não pôde enumerar zonas associadas à GPU ({ex.GetType().Name})");
        }

        return result;
    }

    private GpuDevice? FindGpuForThermalZone(string zonePath, string linkedDevicePath, string type)
    {
        foreach (var device in _devices)
        {
            if (device.CanonicalDevicePath is null) continue;
            if (string.Equals(linkedDevicePath, device.CanonicalDevicePath, StringComparison.Ordinal) ||
                IsPathWithin(linkedDevicePath, device.CanonicalDevicePath) ||
                IsPathWithin(zonePath, device.CanonicalDevicePath))
                return device;
        }

        var normalizedType = type.ToLowerInvariant();
        var driverMatches = _devices.Where(device =>
            device.Driver != "não identificado" &&
            normalizedType.Contains(device.Driver.ToLowerInvariant(), StringComparison.Ordinal)).ToArray();
        if (driverMatches.Length == 1) return driverMatches[0];

        // Só usa o nome genérico quando há uma única placa; com mais de uma GPU,
        // associação ambígua poderia atribuir temperatura de outro componente.
        return normalizedType.Contains("gpu", StringComparison.Ordinal) && _devices.Count == 1
            ? _devices[0]
            : null;
    }

    private static string? ReadPciBusId(string devicePath)
    {
        if (LinuxGpuSensorReader.TryReadText(Path.Combine(devicePath, "uevent"), out var uevent))
        {
            var slot = uevent.Split('\n')
                .FirstOrDefault(line => line.StartsWith("PCI_SLOT_NAME=", StringComparison.Ordinal));
            if (slot is not null)
                return slot["PCI_SLOT_NAME=".Length..].Trim();
        }

        var canonicalPath = ResolvePath(devicePath);
        var lastSegment = Path.GetFileName(canonicalPath);
        return lastSegment.Count(character => character == ':') == 2 && lastSegment.Contains('.')
            ? lastSegment
            : null;
    }

    private static string? GetDriverName(string devicePath)
    {
        try
        {
            return new DirectoryInfo(Path.Combine(devicePath, "driver"))
                .ResolveLinkTarget(returnFinalTarget: true)?.Name;
        }
        catch (Exception ex) when (LinuxGpuSensorReader.IsExpectedSysfsFailure(ex))
        {
            return null;
        }
    }

    private static string ResolvePath(string path)
    {
        try
        {
            var target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName ?? Path.GetFullPath(path);
        }
        catch (Exception ex) when (LinuxGpuSensorReader.IsExpectedSysfsFailure(ex))
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }
    }

    private static bool IsPathWithin(string candidate, string parent)
    {
        var normalizedParent = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.Equals(parent, StringComparison.Ordinal) ||
               candidate.StartsWith(normalizedParent, StringComparison.Ordinal);
    }

    private static bool IsGpuHwmonName(string name)
    {
        var normalized = name.ToLowerInvariant();
        return normalized.StartsWith("amdgpu", StringComparison.Ordinal) ||
               normalized.StartsWith("nouveau", StringComparison.Ordinal) ||
               normalized.StartsWith("nvidia", StringComparison.Ordinal) ||
               normalized == "i915" || normalized.StartsWith("i915_gt", StringComparison.Ordinal) ||
               normalized == "xe" || normalized.StartsWith("xe_", StringComparison.Ordinal);
    }

    private static bool HwmonNameMatchesDriver(string hwmonName, string driverName)
    {
        var name = hwmonName.ToLowerInvariant();
        var driver = driverName.ToLowerInvariant();
        return driver switch
        {
            "amdgpu" => name.StartsWith("amdgpu", StringComparison.Ordinal),
            "nouveau" => name.StartsWith("nouveau", StringComparison.Ordinal),
            "nvidia" => name.StartsWith("nvidia", StringComparison.Ordinal),
            "i915" => name == "i915" || name.StartsWith("i915_gt", StringComparison.Ordinal),
            "xe" => name == "xe" || name.StartsWith("xe_", StringComparison.Ordinal),
            _ => false
        };
    }

    private static string BuildDeviceLabel(string vendor, string driver)
    {
        var vendorLabel = vendor.ToLowerInvariant() switch
        {
            "0x1002" => "AMD GPU",
            "0x10de" => "NVIDIA GPU",
            "0x8086" => "Intel GPU",
            _ => $"GPU {vendor}"
        };
        return driver == "não identificado" ? vendorLabel : $"{vendorLabel} ({driver})";
    }

    private static bool IsCardDirectory(string name) =>
        name.StartsWith("card", StringComparison.Ordinal) && name.Length > 4 && name[4..].All(char.IsAsciiDigit);

    private static int ParseCardNumber(string name) =>
        int.TryParse(name.AsSpan("card".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : int.MaxValue;

    internal static string NormalizePciBusId(string value)
    {
        var components = value.Trim().ToLowerInvariant().Split(':');
        if (components.Length != 3) return value.Trim().ToLowerInvariant();
        var domain = uint.TryParse(components[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedDomain)
            ? parsedDomain.ToString("x4", CultureInfo.InvariantCulture)
            : components[0].PadLeft(4, '0');
        return $"{domain}:{components[1].PadLeft(2, '0')}:{components[2]}";
    }
}
