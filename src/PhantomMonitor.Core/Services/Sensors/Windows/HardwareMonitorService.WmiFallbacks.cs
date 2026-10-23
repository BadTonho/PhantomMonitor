using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using PhantomMonitor.Models;
using LibreHardwareMonitor.Hardware;
using SType = PhantomMonitor.Models.SensorType;

namespace PhantomMonitor.Services;

public sealed partial class HardwareMonitorService
{
    private void ApplyCpuClockFallback(SensorData data, List<string> debugInfo)
    {
        if (data.Values.TryGetValue(SType.CpuClock, out var clock) && double.IsFinite(clock) && clock > 0)
            return;

        try
        {
            var activeProcessorCount = GetActiveProcessorCount(ushort.MaxValue);
            if (activeProcessorCount == 0 || activeProcessorCount > int.MaxValue) return;

            var processorInfo = new ProcessorPowerInformation[(int)activeProcessorCount];
            var outputLength = checked(Marshal.SizeOf<ProcessorPowerInformation>() * processorInfo.Length);
            var status = CallNtPowerInformation(11, IntPtr.Zero, 0, processorInfo, outputLength);
            if (status != 0)
            {
                LogCpuClockFallbackWarning(debugInfo, $"CallNtPowerInformation retornou NTSTATUS 0x{status:X8}");
                return;
            }

            var readings = processorInfo.Where(item => item.CurrentMhz > 0).Select(item => (double)item.CurrentMhz).ToArray();
            if (readings.Length == 0)
            {
                LogCpuClockFallbackWarning(debugInfo, "Windows não retornou frequências positivas");
                return;
            }

            data.Values[SType.CpuClock] = readings.Average();
            data.Labels[SType.CpuClock] = "Clock da CPU (aprox. Windows)";
            debugInfo.Add($"[CPU] Clock geral aproximado pelo Windows = {readings.Average():F0} MHz; não representa clocks individuais.");
        }
        catch (Exception ex)
        {
            LogCpuClockFallbackWarning(debugInfo, $"Falha na leitura de frequência do Windows: {ex.Message}");
        }
    }

    private void LogCpuClockFallbackWarning(List<string> debugInfo, string message)
    {
        if (_cpuClockFallbackWarningLogged) return;
        _cpuClockFallbackWarningLogged = true;
        debugInfo.Add($"[CPU] Clock aproximado indisponível: {message}");
    }

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

    private void ReadBattery(SensorData data, List<string> debugInfo)
    {
        if (DateTime.UtcNow - _lastBatteryRead >= BatteryInterval)
        {
            _lastBatteryRead = DateTime.UtcNow;
            _cachedBattery = null;
            try { _cachedBattery = ReadBatteryFromWmi(); }
            catch (Exception ex) { ErrorLog.Write("WMI.Battery", ex); }
        }

        var b = _cachedBattery;
        if (b == null) return;

        if (b.Level > 0)
        {
            data.Values[SType.BatteryLevel] = b.Level;
            data.Labels[SType.BatteryLevel] = "Bateria (carga estimada)";
        }
        if (b.Voltage > 0)
        {
            data.Values[SType.BatteryVoltage] = b.Voltage;
            data.Labels[SType.BatteryVoltage] = "Bateria (tensão)";
        }
        if (b.Power != 0)
        {
            data.Values[SType.BatteryPower] = b.Power;
            data.Labels[SType.BatteryPower] = "Bateria (fluxo de potência)";
        }
        if (b.Wear > 0)
        {
            data.Values[SType.BatteryWearLevel] = b.Wear;
            data.Labels[SType.BatteryWearLevel] = "Bateria (desgaste)";
        }
        data.Values[SType.BatteryAcPower] = b.AcPower;
        data.Labels[SType.BatteryAcPower] = b.AcPower > 0 ? "Bateria (fonte AC conectada)" : "Bateria (fonte AC desconectada)";

        debugInfo.Add($"[WMI] Bateria: nível={b.Level:F0}% tensão={b.Voltage:F2} V potência={b.Power:F2} W desgaste={b.Wear:F1}% AC={b.AcPower:F0}");
    }

    private BatterySnapshot? ReadBatteryFromWmi()
    {
        double? level = null, voltage = null, power = null, wear = null;
        double acPower = 0;

        const string batteryStatusClass = "BatteryStatus";
        if (!IsWmiClassUnsupported(@"root\WMI", batteryStatusClass))
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryStatus");
                using var results = searcher.Get();
                foreach (var obj in results)
                {
                    using (obj)
                    {
                        var dischargeW = WmiProp(obj, "DischargeRate") / 1000.0;
                        var chargeW = WmiProp(obj, "ChargeRate") / 1000.0;
                        power = dischargeW > 0 ? dischargeW : -chargeW;
                        var mv = WmiProp(obj, "Voltage");
                        if (mv > 0) voltage = mv / 1000.0;
                        acPower = WmiProp(obj, "PowerOnline") != 0 ? 1 : 0;
                    }
                    break;
                }
            }
            catch (Exception ex) { HandleOptionalWmiException(@"root\WMI", batteryStatusClass, "WMI.Battery.Status", ex); }
        }

        const string win32BatteryClass = "Win32_Battery";
        if (!IsWmiClassUnsupported("root\\cimv2", win32BatteryClass))
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                using var results = searcher.Get();
                foreach (var obj in results)
                {
                    using (obj)
                    {
                        var pct = WmiProp(obj, "EstimatedChargeRemaining");
                        if (pct > 0) level = pct;
                        if (voltage is not > 0)
                        {
                            var mv = WmiProp(obj, "DesignVoltage");
                            if (mv > 0) voltage = mv / 1000.0;
                        }
                        if (!wear.HasValue)
                        {
                            var full = WmiProp(obj, "FullChargeCapacity");
                            var design = WmiProp(obj, "DesignCapacity");
                            if (full > 0 && design > 0) wear = Math.Clamp((1 - full / design) * 100.0, 0, 100);
                        }
                    }
                    break;
                }
            }
            catch (Exception ex) { HandleOptionalWmiException("root\\cimv2", win32BatteryClass, "WMI.Battery.Win32", ex); }
        }

        if (!wear.HasValue)
        {
            var full = ReadOptionalWmiFirstNumeric(@"root\WMI", "BatteryFullChargedCapacity",
                "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity", "WMI.Battery.Wear");
            var design = ReadOptionalWmiFirstNumeric(@"root\WMI", "BatteryStaticData",
                "SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity", "WMI.Battery.Wear");
            if (full > 0 && design > 0) wear = Math.Clamp((1 - full / design) * 100.0, 0, 100);
        }

        if (!level.HasValue && !voltage.HasValue && !power.HasValue) return null;
        return new BatterySnapshot(level ?? 0, voltage ?? 0, power ?? 0, wear ?? 0, acPower);
    }

    private bool IsWmiClassUnsupported(string scope, string className) =>
        _unsupportedWmiClasses.Contains($"{scope}:{className}");

    private void HandleOptionalWmiException(string scope, string className, string context, Exception exception)
    {
        if (exception is ManagementException { ErrorCode: ManagementStatus.InvalidClass })
        {
            _unsupportedWmiClasses.Add($"{scope}:{className}");
            return;
        }

        ErrorLog.Write(context, exception);
    }

    private double ReadOptionalWmiFirstNumeric(string scope, string className, string query, string property, string context)
    {
        if (IsWmiClassUnsupported(scope, className)) return 0;
        try { return WmiFirstNumeric(scope, query, property); }
        catch (Exception ex)
        {
            HandleOptionalWmiException(scope, className, context, ex);
            return 0;
        }
    }

    /// <summary>Propriedade WMI tolerante: propriedade ausente do driver devolve fallback (não lança).</summary>

    private static double WmiProp(ManagementBaseObject obj, string name, double fallback = 0)
    {
        try
        {
            if (obj.Properties[name]?.Value is IConvertible c)
                return c.ToDouble(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            _ = ex; // driver não expõe a propriedade: segue com fallback
        }
        return fallback;
    }

    private static object? WmiValue(ManagementBaseObject obj, string name)
    {
        try { return obj.Properties[name]?.Value; }
        catch { return null; }
    }

    /// <summary>Primeiro valor numérico de uma consulta WMI (0 se não houver resultado).</summary>

    private static double WmiFirstNumeric(string scope, string query, string property)
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();
        foreach (var obj in results)
        {
            using (obj) return WmiProp(obj, property);
        }
        return 0;
    }

    // ---------- Erros de hardware WHEA ----------

    /// <summary>
    /// WHEA (paridade HWiNFO): publica a série canônica <c>WheaErrorCount</c> — nº de
    /// eventos do provedor "Microsoft-Windows-WHEA-Logger" no log System desde o
    /// início da sessão. Contagem em 0 é leitura REAL (não "ausente"): alertas
    /// "Above &gt; 0" disparam em qualquer erro novo; cover Windows apenas.
    /// Consultas a cada 10 s, direção reversa com early-break (o provedor é estável;
    /// varrer o log inteiro a cada ciclo seria desperdício). Teto defensivo de 5000
    /// eventos por consulta (log gigante/repasse de hardware antigo).
    /// </summary>

    private void ReadWheaErrors(SensorData data, List<string> debugInfo)
    {
        if (DateTime.UtcNow - _lastWheaRead < WheaInterval)
        {
            // Valor congelado entre consultas continua sendo publicado (série presente).
            data.SetValue(SType.WheaErrorCount, _wheaSessionCount, "Erros de hardware WHEA (sessão)");
            return;
        }
        _lastWheaRead = DateTime.UtcNow;
        _wheaSessionCount = 0;

        try
        {
            if (_wheaSessionStart is not { } start) return; // Start() não rodou

            var query = new EventLogQuery(
                "System",
                PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger']]]");
            query.ReverseDirection = true; // do mais novo para o mais velho

            using var reader = new EventLogReader(query);
            var scanned = 0;
            while (reader.ReadEvent() is { } entry)
            {
                using (entry)
                {
                    scanned++;
                    if (entry.TimeCreated is not { } created) continue;
                    if (created < start)
                        break; // direção reversa: os próximos são mais antigos
                    _wheaSessionCount++;
                }
                if (scanned >= 5000) break;
            }

            debugInfo.Add($"[WHEA] Erros de hardware na sessão: {_wheaSessionCount}");
        }
        catch (Exception ex)
        {
            // Provedor ausente/sem permissão de leitura: contador segue em 0 (leitura real).
            ErrorLog.Write("Whea.Reader", ex);
        }
        finally
        {
            data.SetValue(SType.WheaErrorCount, _wheaSessionCount, "Erros de hardware WHEA (sessão)");
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
}
