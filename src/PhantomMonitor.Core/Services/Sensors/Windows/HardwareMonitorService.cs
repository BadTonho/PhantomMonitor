using LibreHardwareMonitor.Hardware;
using PhantomMonitor.Models;
using System.Runtime.Versioning;
using SType = PhantomMonitor.Models.SensorType;

namespace PhantomMonitor.Services;

[SupportedOSPlatform("windows")]
public sealed partial class HardwareMonitorService : ISensorService, IOverlayProcessDiscovery
{
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

    // Sensores customizados (fórmulas do usuário): avaliados no fim de cada snapshot.
    private readonly CustomSensorEngine? _customSensors;

    // Ajuste por série (P9.1): cadência de amostragem + suavização antes das estatísticas.
    private readonly SensorTuningEngine? _sensorTuning;

    private readonly WindowsGpuProcessReader _gpuProcessReader = new();
    private readonly WindowsSensorFallbackReader _sensorFallbackReader = new();
    private readonly WindowsMonitorInventoryReader _monitorInventoryReader = new();

    public event Action<SensorData>? SensorUpdated;
    public bool IsRunning { get; private set; }
    public bool OverlayProcessDiscoveryEnabled
    {
        get => _fpsMonitor.OverlayProcessDiscoveryEnabled;
        set => _fpsMonitor.OverlayProcessDiscoveryEnabled = value;
    }
    public IReadOnlyList<PresentingProcess> GetRecentPresentingProcesses() =>
        _fpsMonitor.GetRecentPresentingProcesses();
    public string OverlayDiscoveryDiagnostic => _fpsMonitor.EtwDiag;

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
            _sensorFallbackReader.StartSession();
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

        _sensorFallbackReader.ApplyCpuClockFallback(data, debugInfo);
        _sensorFallbackReader.ApplyCpuTempFallback(data, debugInfo);
        _sensorFallbackReader.ReadBattery(data, debugInfo);
        _sensorFallbackReader.ReadWheaErrors(data, debugInfo);

        data.Monitors = _monitorInventoryReader.GetMonitors();

        data.GpuProcesses = _gpuProcessReader.GetGpuProcesses();

        var activeGamePid = _fpsMonitor.HasData
            ? _fpsMonitor.TargetProcessId
            : _fpsMonitor.TargetProcessId > 0 && data.GpuProcesses.Any(p => p.Name.Equals(_fpsMonitor.TargetProcessName, StringComparison.OrdinalIgnoreCase))
                ? _fpsMonitor.TargetProcessId
                : 0;
        var activeGameName = activeGamePid > 0 ? _fpsMonitor.TargetProcessName : "";
        _sensorFallbackReader.ApplyRamFallbacks(data, debugInfo, activeGamePid, activeGameName);

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

    public void Dispose()
    {
        if (_disposed) return;

        Stop();

        lock (_sync)
        {
            _disposed = true;
            _timer.Dispose();
            _fpsMonitor.Dispose();
            _sensorFallbackReader.Dispose();
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
