using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using PhantomMonitor.Models;
using System.Runtime.Versioning;
using LhmSensorType = LibreHardwareMonitor.Hardware.SensorType;
using SType = PhantomMonitor.Models.SensorType;

namespace PhantomMonitor.Services;

[SupportedOSPlatform("windows")]
public sealed partial class HardwareMonitorService : ISensorService
{
    private static readonly TimeSpan WmiTempInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GpuProcessInterval = TimeSpan.FromSeconds(3);
    private static readonly Regex GpuEngineRegex = new(@"pid_(\d+)_.*engtype_(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly SType[] GpuAvailabilityTypes =
    [
        SType.GpuCore,
        SType.GpuMemoryUsed,
        SType.GpuMemoryTotal,
        SType.GpuTemperature,
        SType.GpuHotSpot,
        SType.GpuVramTemperature
    ];

    private readonly Computer _computer;
    private readonly Timer _timer;
    private readonly int _updateIntervalMs;
    private readonly FpsMonitor _fpsMonitor = new();

    // Serializa Start/Stop/Update/Dispose (o timer roda em thread do pool).
    private readonly object _sync = new();
    private volatile bool _disposed;
    private SensorData _current = new();

    // Estatísticas por série da sessão (min/max/avg; zeradas pelo usuário).
    private readonly SeriesStatsTracker _seriesStats = new();

    // Prioridade do sensor escolhido por tipo na rodada atual (acessado só dentro de _sync).
    private readonly Dictionary<SType, int> _priority = [];

    // Instância "#i" por nó de hardware: GPU (dedicada = 1), discos e interfaces de rede
    // (cursores por família, resetados a cada ciclo — a ordem do enumerador do LHM é estável).
    private readonly Dictionary<IHardware, int> _hardwareInstanceFor = [];
    private readonly Dictionary<int, HashSet<SType>> _gpuMetricsReported = [];
    private int _diskInstanceCursor;
    private int _netInstanceCursor;

    // Slots por nome para series que não têm instância numérica clara
    // (rails "+12V", ventoinhas "Fan #2", zonas VRM, dominios de potência da CPU...).
    private readonly Dictionary<string, int> _seriesSlots = new(StringComparer.Ordinal);

    // Total acumulado de rede na sessão (NetworkUpTotal/NetworkDownTotal).
    private DateTime? _lastNetSampleUtc;
    private double _netUpSessionBytes;
    private double _netDownSessionBytes;

    // Fallbacks e caches (evitam consultas WMI pesadas a cada ciclo)
    private PerformanceCounter? _ramCounter;
    private DateTime _lastWmiTemp = DateTime.MinValue;
    private double _cachedWmiTemp;
    private DateTime _lastGpuProcesses = DateTime.MinValue;
    private List<GpuProcessInfo> _cachedGpuProcesses = [];
    private readonly Dictionary<int, string> _pidNames = [];
    private readonly HashSet<string> _unsupportedWmiClasses = new(StringComparer.OrdinalIgnoreCase);

    // Bateria (P6.4): leitura WMI a cada 10 s é mais que suficiente — carga/tensão
    // mudam devagar, e a consulta atravessa WMI COM, que custa mais que o ciclo.
    private BatterySnapshot? _cachedBattery;
    private DateTime _lastBatteryRead = DateTime.MinValue;
    private static readonly TimeSpan BatteryInterval = TimeSpan.FromSeconds(10);

    // Sensores customizados (fórmulas do usuário): avaliados no fim de cada snapshot.
    private readonly CustomSensorEngine? _customSensors;

    // Ajuste por série (P9.1): cadência de amostragem + suavização antes das estatísticas.
    private readonly SensorTuningEngine? _sensorTuning;

    // WHEA (paridade HWiNFO): contagem de erros de hardware desde o início da sessão.
    // Consulta ao log de eventos é direção reversa com early-break (a assinatura do
    // log é estável; reler tudo a cada ciclo encheria buffers desnecessariamente).
    private DateTime? _wheaSessionStart;
    private int _wheaSessionCount;
    private DateTime _lastWheaRead = DateTime.MinValue;
    private static readonly TimeSpan WheaInterval = TimeSpan.FromSeconds(10);

    // Identidade dos monitores (EDID via root\wmi): cache de 60 s — painéis/slot
    // de monitor não mudam a cada ciclo, e a consulta atravessa WMI COM por fullscreen.
    private List<MonitorInfo> _cachedMonitors = [];
    private DateTime _lastMonitorsRead = DateTime.MinValue;
    private static readonly TimeSpan MonitorsInterval = TimeSpan.FromSeconds(60);
    private bool _monitorWmiFailureLogged;

    public event Action<SensorData>? SensorUpdated;
    public bool IsRunning { get; private set; }

    /// <summary>Pausa (P6.7): o timer continua, mas os ciclos pulam sem publicar evento.</summary>
    public bool Paused { get; private set; }

    public void SetPaused(bool paused)
    {
        lock (_sync) Paused = paused;
    }

    public HardwareMonitorService(int updateIntervalMs = 1000, CustomSensorEngine? customSensors = null, SensorTuningEngine? sensorTuning = null)
    {
        _updateIntervalMs = Math.Max(100, updateIntervalMs);
        _customSensors = customSensors;
        _sensorTuning = sensorTuning;
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
            _wheaSessionStart = DateTime.Now; // EventLogReader.TimeCreated é hora local
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

    /// <summary>Zera as estatísticas por série da sessão (bandeja/inventário).</summary>
    public void ResetSeriesStats()
    {
        lock (_sync) _seriesStats.Reset();
    }

    private void Update()
    {
        // Pausa (P6.7): nenhum ciclo — sem evento (CSV/JSON/SDK/HTTP/overlay congelam
        // no último snapshot), sem WMI/ETW a mais. O set check volta a ler _current.
        if (Paused) return;

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

        // GPUs integradas primeiro: a GPU dedicada (processada depois) prevalece nos valores
        // canônicos (índice 0). Para os índices de instância (#1, #2...), a dedicada é a GPU 1.
        _hardwareInstanceFor.Clear();
        _gpuMetricsReported.Clear();
        _diskInstanceCursor = 0;
        _netInstanceCursor = 0;
        var instance = 1;
        foreach (var gpu in _computer.Hardware
                     .OrderBy(h => h.HardwareType == HardwareType.GpuIntel ? 1 : 0)
                     .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
        {
            _hardwareInstanceFor[gpu] = instance++;
        }

        foreach (var hardware in _computer.Hardware.OrderBy(h => h.HardwareType == HardwareType.GpuIntel ? 0 : 1))
        {
            ProcessHardware(hardware, data, debugInfo, depth: 0);
        }

        AppendGpuAvailabilityDiagnostics(data, debugInfo);

        ApplyCpuClockFallback(data, debugInfo);
        ApplyCpuTempFallback(data, debugInfo);
        ApplyRamFallbacks(data, debugInfo);
        ReadBattery(data, debugInfo);
        ReadWheaErrors(data, debugInfo);

        data.Monitors = GetMonitors();

        data.GpuProcesses = GetGpuProcesses();

        // Totais de rede da sessão: acumula velocidade observada x tempo desde a amostra anterior.
        if (_lastNetSampleUtc is { } last && data.GetValue(SType.NetworkUp) + data.GetValue(SType.NetworkDown) > 0)
        {
            var elapsed = DateTime.UtcNow - last;
            _netUpSessionBytes += data.GetValue(SType.NetworkUp) * elapsed.TotalSeconds;
            _netDownSessionBytes += data.GetValue(SType.NetworkDown) * elapsed.TotalSeconds;
        }
        _lastNetSampleUtc = DateTime.UtcNow;
        if (_netUpSessionBytes > 0 || _netDownSessionBytes > 0)
        {
            data.SetValue(SType.NetworkUpTotal, _netUpSessionBytes, "Total enviado (sessão)");
            data.SetValue(SType.NetworkDownTotal, _netDownSessionBytes, "Total recebido (sessão)");
        }

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

            // Métricas extras do PresentMon (P6.5): 0 = coluna ausente nesta versão
            // (o CSV/JSON vazio em CpuTemp segue o mesmo contrato de <= 0 = indisponível).
            data.Values[SType.GpuBusy] = _fpsMonitor.GpuBusy;
            data.Values[SType.CpuBusy] = _fpsMonitor.CpuBusy;
            data.Values[SType.CpuWait] = _fpsMonitor.CpuWait;
            data.Values[SType.PresentLatency] = _fpsMonitor.PresentLatencyMs;
            data.Values[SType.FpsP1] = _fpsMonitor.FpsP1;
            data.Values[SType.FpsP99] = _fpsMonitor.FpsP99;
            data.Values[SType.FrametimeP1] = _fpsMonitor.FrametimeP1;
            data.Values[SType.FrametimeP99] = _fpsMonitor.FrametimeP99;
        }

        // Estatísticas por série: uma amostra por snapshot publicado, anexada ao
        // snapshot (inventário e JSON exibem min/máx/média da sessão).
        // Sensores customizados avaliam ANTES do commit: as fórmulas podem referenciar
        // qualquer série (LHM, FPS/present, NETWORK…) e entram nas estatísticas/exportações.
        _customSensors?.Evaluate(data);

        // Ajuste por série (P9.1): cadência + suavização; séries repetidas não
        // entram nas estatísticas (a média da sessão amostra na cadência delas).
        var repeated = _sensorTuning?.Apply(data);

        // Publicação: pré-computa as chaves de série do snapshot. Aqui não há mais
        // escrita em Values/Series, então os consumidores por tick compartilham a
        // mesma lista (cada um reconstruía e reordenava a própria antes).
        data.CachedSeriesKeys = data.GetSeriesKeys();

        _seriesStats.Commit(data, repeated);
        data.SeriesStats = _seriesStats.CopySnapshot();

        return data;
    }

    private void ProcessHardware(IHardware hardware, SensorData data, List<string> debugInfo, int depth, int gpuInstance = 0)
    {
        var indent = new string(' ', depth * 2);
        debugInfo.Add($"{indent}[HWR] {hardware.HardwareType}: {hardware.Name}");

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

    // ---------- Fallbacks ----------

    private bool _cpuClockFallbackWarningLogged;

    /// <summary>
    /// Clock geral aproximado via Windows quando o LHM não expõe frequência positiva.
    /// CurrentMhz acompanha o limite máximo multiplicado pelo throttling atual; não é
    /// uma leitura por núcleo e nunca preenche as séries CpuClock#i.
    /// </summary>
#pragma warning disable CS0649 // O Windows preenche o buffer retornado por CallNtPowerInformation.
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }
#pragma warning restore CS0649

    [DllImport("PowrProf.dll", EntryPoint = "CallNtPowerInformation")]
    private static extern int CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, int inputBufferLength,
        [Out] ProcessorPowerInformation[] outputBuffer, int outputBufferLength);

    [DllImport("kernel32.dll", EntryPoint = "GetActiveProcessorCount")]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);

    private sealed record BatterySnapshot(double Level, double Voltage, double Power, double Wear, double AcPower);

    /// <summary>
    /// Bateria (P6.4): só publica séries quando HÁ bateria (notebook/UPS) — desktop fica
    /// sem nenhuma série da família BAT. Fontes em cascata: `root\WMI` (BatteryStatus:
    /// tensão/potência/AC; BatteryFullChargedCapacity + BatteryStaticData: desgaste) e
    /// `Win32_Battery` (nível estimado + resultado de respatilha para tensão/desgaste).
    /// Consultas são a cada 10 s; `SELECT *` com acesso defensivo às propriedades porque
    /// drivers de bateria variam o que expõem.
    /// </summary>
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

    // ---------- Identidade de monitores (EDID via WMI) ----------

    /// <summary>
    /// Monitores conectados, decodificados de <c>root\wmi</c>: <c>WmiMonitorID</c>
    /// (código do fabricante/Modelo\serial/ano+semana do EDID) e
    /// <c>WmiMonitorBasicDisplayParams</c> (tamanho físico → diagonal; bit
    /// VideoInputType = primário). Cache de 60 s; ordena para estabilidade entre
    ///ciclos. Sem monitores reportados (VMs/RDP): lista vazia e nenhuma falha.
    /// </summary>
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
