using System.Diagnostics;
using System.Collections;
using System.Globalization;
using System.Management;
using System.Text;
using PhantomMonitor.Models;
using LibreHardwareMonitor.Hardware;
using SType = PhantomMonitor.Models.SensorType;

namespace PhantomMonitor.Services;

public sealed partial class HardwareMonitorService
{
    private List<MonitorInfo> GetMonitors()
    {
        if (DateTime.UtcNow - _lastMonitorsRead < MonitorsInterval)
            return _cachedMonitors;

        _lastMonitorsRead = DateTime.UtcNow;
        var result = new List<MonitorInfo>();

        try
        {
            // 1) Tamanho físico e flag de primário por InstanceName.
            var sizes = new Dictionary<string, (double In, bool Primary)>(StringComparer.OrdinalIgnoreCase);
            using (var sizeSearcher = new ManagementObjectSearcher(@"root\wmi",
                       "SELECT * FROM WmiMonitorBasicDisplayParams"))
            using (var items = sizeSearcher.Get())
            {
                foreach (var obj in items)
                {
                    using (obj)
                    {
                        var instance = WmiValue(obj, "InstanceName")?.ToString() ?? "";
                        if (instance.Length == 0) continue;

                        var hCm = WmiProp(obj, "MaxHorizontalImageSize");
                        var vCm = WmiProp(obj, "MaxVerticalImageSize");
                        var inches = hCm > 0 && vCm > 0
                            ? Math.Ceiling(Math.Sqrt(hCm * hCm + vCm * vCm) / 2.54 * 10.0) / 10.0
                            : 0.0;
                        var isPrimary = WmiProp(obj, "VideoInputType", 0) % 2 == 1;
                        sizes[instance] = (inches, isPrimary);
                    }
                }
            }

            // 2) Identidade EDID por InstanceName (somente monitores ativos).
            using (var idSearcher = new ManagementObjectSearcher(@"root\wmi",
                       "SELECT * FROM WmiMonitorID"))
            using (var items = idSearcher.Get())
            {
                foreach (var obj in items)
                {
                    using (obj)
                    {
                        if (WmiProp(obj, "Active", 1) == 0) continue; // monitor "fantasma" no registro

                        var instance = WmiValue(obj, "InstanceName")?.ToString() ?? "";
                        var info = new MonitorInfo
                        {
                            InstanceName = instance,
                            Manufacturer = DecodeEdidString(WmiValue(obj, "ManufacturerName")),
                            Model = DecodeUserFriendlyName(obj),
                            ProductCode = DecodeProductCode(WmiValue(obj, "ProductCodeID")),
                            Serial = DecodeEdidString(WmiValue(obj, "SerialNumberID")),
                            DiagonalInches = sizes.TryGetValue(instance, out var size) ? size.In : 0.0,
                            Primary = sizes.TryGetValue(instance, out var size2) && size2.Primary,
                            WeekOfManufacture = (int)WmiProp(obj, "WeekOfManufacture"),
                            YearOfManufacture = (int)WmiProp(obj, "YearOfManufacture")
                        };
                        result.Add(info);
                    }
                }
            }

            // Estável entre ciclos (o WMI come entrega de qualquer ordem).
            result.Sort((a, b) => string.CompareOrdinal(a.InstanceName, b.InstanceName));
        }
        catch (Exception ex)
        {
            if (!_monitorWmiFailureLogged)
            {
                _monitorWmiFailureLogged = true;
                ErrorLog.Write("WMI.MonitorId", $"Aviso: consulta EDID indisponível nesta execução. {ex.Message}");
            }
        }

        return _cachedMonitors = result;
    }

    /// <summary>Array UInt16 do EDID ( ManufacturerName/SerialNumberID ) → texto ASCII até o terminador 0.</summary>

    private static string DecodeEdidString(object? raw)
    {
        if (raw is not IEnumerable codes) return "";

        var sb = new StringBuilder(8);
        try
        {
            foreach (var code in codes)
            {
                var v = Convert.ToUInt16(code, CultureInfo.InvariantCulture);
                if (v == 0) break; // terminador/padding
                if (v is >= 32 and <= 126) sb.Append((char)v);
            }
        }
        catch { return ""; }
        return sb.ToString();
    }

    /// <summary>Modelo do painel: UserFriendlyName (tamanho real na prop @Length) e fallback do ProductCodeID.</summary>

    private static string DecodeUserFriendlyName(ManagementBaseObject obj)
    {
        try
        {
            var name = WmiValue(obj, "UserFriendlyName") switch
            {
                char[] chars => (object?)chars,
                string str => str,
                _ => null
            };
            if (name is char[] array)
            {
                var length = (int)WmiProp(obj, "UserFriendlyNameLength");
                var sb = new StringBuilder(length > 0 && length <= array.Length ? length : array.Length);
                foreach (var c in array)
                {
                    if (c == '\0') break;
                    sb.Append(c);
                }
                var friendly = sb.ToString().Trim();
                if (friendly.Length > 0) return friendly;
            }

            // Painel sem UserFriendlyName (alguns laptops/DUMY devices): usa o product code.
            var code = DecodeProductCode(WmiValue(obj, "ProductCodeID"));
            return code.Length > 0 ? code : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>ProductCodeID → texto do EDID quando printable, senão número decimal (padrão das ferramentas).</summary>

    private static string DecodeProductCode(object? raw)
    {
        if (raw is not IEnumerable codes) return "";

        try
        {
            var values = new List<ushort>();
            foreach (var code in codes)
            {
                var value = Convert.ToUInt16(code, CultureInfo.InvariantCulture);
                if (value == 0) break;
                values.Add(value);
            }

            var printable = new StringBuilder(values.Count);
            foreach (var value in values)
            {
                if (value is >= 32 and <= 126)
                    printable.Append((char)value);
            }
            if (printable.Length > 0) return printable.ToString();

            var numeric = new StringBuilder(values.Count * 5);
            foreach (var value in values)
            {
                if (numeric.Length > 0) numeric.Append('-');
                numeric.Append(value.ToString(CultureInfo.InvariantCulture));
            }
            return numeric.ToString();
        }
        catch { return ""; }
    }

    // ---------- Processos na GPU ----------
}
