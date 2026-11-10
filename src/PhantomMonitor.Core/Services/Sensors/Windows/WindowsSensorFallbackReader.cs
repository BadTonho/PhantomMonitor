using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PhantomMonitor.Models;
using SType = PhantomMonitor.Models.SensorType;

namespace PhantomMonitor.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSensorFallbackReader : IDisposable
{
    private static readonly TimeSpan WmiTempInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BatteryInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WheaInterval = TimeSpan.FromSeconds(10);
    private bool _cpuClockFallbackWarningLogged;
    private DateTime _lastWmiTemp = DateTime.MinValue;
    private double _cachedWmiTemp;
    private PerformanceCounter? _ramCounter;
    private readonly HashSet<string> _unsupportedWmiClasses = new(StringComparer.OrdinalIgnoreCase);
    private BatterySnapshot? _cachedBattery;
    private DateTime _lastBatteryRead = DateTime.MinValue;
    private DateTime? _wheaSessionStart;
    private int _wheaSessionCount;
    private DateTime _lastWheaRead = DateTime.MinValue;
    private bool _ramSpecLoaded;
    private double _cachedRamFrequencyMhz;
    private double _cachedRamCasLatency;

    public void StartSession() => _wheaSessionStart = DateTime.Now;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("PowrProf.dll", EntryPoint = "CallNtPowerInformation")]
    private static extern int CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, int inputBufferLength,
        [Out] ProcessorPowerInformation[] outputBuffer, int outputBufferLength);

    [DllImport("kernel32.dll", EntryPoint = "GetActiveProcessorCount")]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);

    private sealed record BatterySnapshot(double Level, double Voltage, double Power, double Wear, double AcPower);
    internal void ApplyCpuClockFallback(SensorData data, List<string> debugInfo)
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

    internal void ApplyCpuTempFallback(SensorData data, List<string> debugInfo)
    {
        if (data.Values.ContainsKey(SType.CpuTemperature)) return;

        // WMI é caro e raramente funciona: consulta no máximo a cada 10 s e reaproveita o resultado.
        if (DateTime.UtcNow - _lastWmiTemp >= WmiTempInterval)
        {
            _lastWmiTemp = DateTime.UtcNow;
            _cachedWmiTemp = 0;

            try { _cachedWmiTemp = ReadCpuTempFromThermalZone(); }
            catch (Exception ex) { ErrorLog.WriteThrottled("WMI.ThermalZone", ex); }

            if (_cachedWmiTemp <= 0)
            {
                try { _cachedWmiTemp = ReadCpuTempFromPerfCounter(); }
                catch (Exception ex) { ErrorLog.WriteThrottled("WMI.ThermalZonePerf", ex); }
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

    internal void ApplyRamFallbacks(SensorData data, List<string> debugInfo, int activeGamePid = 0, string activeGameName = "")
    {
        // LibreHardwareMonitor expõe Memory Used/Available em GB.
        if (data.Values.TryGetValue(SType.RamUsed, out var used) &&
            data.Values.TryGetValue(SType.RamAvailable, out var avail) &&
            !data.Values.ContainsKey(SType.RamTotal))
        {
            data.Values[SType.RamTotal] = used + avail;
            data.Labels[SType.RamTotal] = "RAM Total (Used + Available)";
        }

        // Sempre lê GlobalMemoryStatusEx para memória comprometida (Commit Charge) e fallback de RAM física.
        try
        {
            var (freeGb, totalGb, committedGb, commitLimitGb) = ReadRamNative();
            if (freeGb > 0 && data.GetValue(SType.RamAvailable) <= 0)
            {
                data.Values[SType.RamAvailable] = freeGb;
                data.Labels[SType.RamAvailable] = "RAM Disponível";
            }
            if (totalGb > 0 && data.GetValue(SType.RamTotal) <= 0)
            {
                data.Values[SType.RamTotal] = totalGb;
                data.Labels[SType.RamTotal] = "RAM Total";
            }
            if (committedGb > 0 && data.GetValue(SType.RamCommitted) <= 0)
            {
                data.Values[SType.RamCommitted] = committedGb;
                data.Labels[SType.RamCommitted] = "Memória comprometida";
            }
            if (commitLimitGb > 0 && data.GetValue(SType.RamCommitLimit) <= 0)
            {
                data.Values[SType.RamCommitLimit] = commitLimitGb;
                data.Labels[SType.RamCommitLimit] = "Limite de memória comprometida";
            }
        }
        catch (Exception ex) { ErrorLog.WriteThrottled("Native.Ram", ex); }

        if (!data.Values.ContainsKey(SType.RamUsed) && data.GetValue(SType.RamTotal) > 0 && data.GetValue(SType.RamAvailable) > 0)
        {
            data.Values[SType.RamUsed] = Math.Max(0, data.GetValue(SType.RamTotal) - data.GetValue(SType.RamAvailable));
            data.Labels[SType.RamUsed] = "RAM Usada";
        }

        EnsurePhysicalMemorySpecLoaded(debugInfo);
        if (_cachedRamFrequencyMhz > 0 && data.GetValue(SType.RamFrequency) <= 0)
        {
            data.Values[SType.RamFrequency] = _cachedRamFrequencyMhz;
            data.Labels[SType.RamFrequency] = "Frequência da RAM";
        }
        if (_cachedRamCasLatency > 0 && data.GetValue(SType.RamCasLatency) <= 0)
        {
            data.Values[SType.RamCasLatency] = _cachedRamCasLatency;
            data.Labels[SType.RamCasLatency] = $"Timings (CL{_cachedRamCasLatency:F0})";
        }

        var cl = data.GetValue(SType.RamCasLatency);
        var trcd = data.GetValue(SType.RamTrcd);
        var trp = data.GetValue(SType.RamTrp);
        var tras = data.GetValue(SType.RamTras);
        if (cl > 0 && trcd > 0 && trp > 0 && tras > 0)
        {
            data.RamTimings = $"{cl:F0}-{trcd:F0}-{trp:F0}-{tras:F0}";
            data.Labels[SType.RamCasLatency] = $"Timings ({data.RamTimings})";
        }
        else if (cl > 0)
        {
            data.RamTimings = $"CL{cl:F0}";
            data.Labels[SType.RamCasLatency] = $"Timings (CL{cl:F0})";
        }

        double gameRamGb = 0;
        if (activeGamePid > 0)
        {
            try
            {
                using var proc = Process.GetProcessById(activeGamePid);
                gameRamGb = Math.Max(0, proc.WorkingSet64 / (1024.0 * 1024.0 * 1024.0));
            }
            catch { gameRamGb = 0; }
        }
        data.SetValue(SType.RamGameUsed, gameRamGb, activeGameName.Length > 0 ? $"RAM Jogo ({activeGameName})" : "RAM Jogo");
        if (data.Values.TryGetValue(SType.RamUsed, out var totalUsedGb))
            data.SetValue(SType.RamSystemUsed, Math.Max(0, totalUsedGb - gameRamGb), "RAM Sistema");

        if (data.GetValue(SType.RamAvailable) > 0 && data.GetValue(SType.RamTotal) > 0 && data.Values.ContainsKey(SType.RamUsage)) return;

        try
        {
            var (freeGb, totalGb, _, _) = ReadRamNative();
            debugInfo.Add($"[RAM] Available = {freeGb:F1} GB / Total = {totalGb:F1} GB");
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
        catch (Exception ex) { ErrorLog.WriteThrottled("WMI.Ram", ex); }

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
            catch (Exception ex) { ErrorLog.WriteThrottled("PerfCounter.Ram", ex); }
        }

        if (!data.Values.ContainsKey(SType.RamUsage) &&
            data.GetValue(SType.RamTotal) > 0 && data.GetValue(SType.RamAvailable) > 0)
        {
            var total = data.GetValue(SType.RamTotal);
            data.Values[SType.RamUsage] = Math.Clamp((total - data.GetValue(SType.RamAvailable)) / total * 100.0, 0, 100);
            data.Labels[SType.RamUsage] = "RAM Uso (calculado)";
        }
    }

    internal void ReadBattery(SensorData data, List<string> debugInfo)
    {
        if (DateTime.UtcNow - _lastBatteryRead >= BatteryInterval)
        {
            _lastBatteryRead = DateTime.UtcNow;
            _cachedBattery = null;
            try { _cachedBattery = ReadBatteryFromWmi(); }
            catch (Exception ex) { ErrorLog.WriteThrottled("WMI.Battery", ex); }
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
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT DischargeRate, ChargeRate, Voltage, PowerOnline FROM BatteryStatus");
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
                using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, DesignVoltage, FullChargeCapacity, DesignCapacity FROM Win32_Battery");
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

        ErrorLog.WriteThrottled(context, exception);
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

    internal static double WmiProp(ManagementBaseObject obj, string name, double fallback = 0)
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

    internal static object? WmiValue(ManagementBaseObject obj, string name)
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

    internal void ReadWheaErrors(SensorData data, List<string> debugInfo)
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
            ErrorLog.WriteThrottled("Whea.Reader", ex);
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

    /// <summary>Memória física via GlobalMemoryStatusEx (GB); cai para WMI se a chamada nativa falhar.</summary>
    private static (double availableGB, double totalGB, double committedGB, double commitLimitGB) ReadRamNative()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0)
                return (
                    status.AvailPhys / 1024.0 / 1024.0 / 1024.0,
                    status.TotalPhys / 1024.0 / 1024.0 / 1024.0,
                    status.TotalPageFile >= status.AvailPageFile ? (status.TotalPageFile - status.AvailPageFile) / 1024.0 / 1024.0 / 1024.0 : 0,
                    status.TotalPageFile / 1024.0 / 1024.0 / 1024.0);
        }
        catch (Exception ex) { ErrorLog.WriteThrottled("Native.Ram", ex); }
        var (availWmi, totalWmi) = ReadRamFromWmi();
        return (availWmi, totalWmi, 0, 0);
    }

    private void EnsurePhysicalMemorySpecLoaded(List<string> debugInfo)
    {
        if (_ramSpecLoaded) return;
        _ramSpecLoaded = true;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed, PartNumber FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            foreach (var obj in results)
            {
                using (obj)
                {
                    var configuredSpeed = WmiProp(obj, "ConfiguredClockSpeed");
                    var speed = WmiProp(obj, "Speed");
                    var freq = configuredSpeed > 0 ? configuredSpeed : speed;
                    if (freq > _cachedRamFrequencyMhz) _cachedRamFrequencyMhz = freq;

                    if (_cachedRamCasLatency <= 0 && WmiValue(obj, "PartNumber") is string part && !string.IsNullOrWhiteSpace(part))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(part.Trim(), @"(?:CL|C)(\d{2})(?:\D|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var cl) && cl is >= 9 and <= 50)
                            _cachedRamCasLatency = cl;
                    }
                }
            }
            if (_cachedRamFrequencyMhz > 0 || _cachedRamCasLatency > 0)
                debugInfo.Add($"[RAM] Spec WMI: {_cachedRamFrequencyMhz:F0} MHz, CL={_cachedRamCasLatency:F0}");
        }
        catch (Exception ex) { ErrorLog.WriteThrottled("WMI.PhysicalMemory", ex); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length; public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

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

    public void Dispose() => _ramCounter?.Dispose();
}
