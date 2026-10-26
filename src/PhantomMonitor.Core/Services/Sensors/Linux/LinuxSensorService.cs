using System.Globalization;
using System.IO;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.Services.Linux;

/// <summary>
/// Provedor nativo de telemetria e sensores para Linux.
/// Lê diretamente do kernel Linux via procfs (/proc) e sysfs (/sys/class/hwmon, /sys/class/drm),
/// sem requerer drivers proprietários nem utilitários de terceiros.
/// </summary>
public sealed partial class LinuxSensorService : ISensorService
{
    private readonly Timer _timer;
    private readonly int _updateIntervalMs;
    private readonly LinuxFpsMonitor _fpsMonitor = new();
    private readonly LinuxCpuMemoryReader _cpuMemoryReader = new();
    private readonly LinuxDiskNetworkReader _diskNetworkReader = new();
    private readonly LinuxGpuSensorReader _gpuSensorReader = new();
    private readonly LinuxHardwareIdentityReader _hardwareIdentityReader = new();
    private List<HardwareIdentity> _hardwareIdentities = [];
    private DateTime _lastHardwareIdentityRefresh = DateTime.MinValue;
    private readonly LinuxHardwarePowerReader _hardwarePowerReader;
    private readonly object _updateLock = new();
    private SensorData _current = new();

    // Diagnóstico de fontes: reconstruído no máximo 1x/s (re-enumera sysfs/DRM por
    // chamada; a cada tick multiplicava I/O em polling rápido). Lista reusada entre
    // snapshots até a próxima reconstrução — a reconstrução gera lista nova antes
    // de publicar, consumidor nunca vê lista parcial.
    private static readonly TimeSpan DiagnosticsRefreshInterval = TimeSpan.FromSeconds(1);
    private DateTime _lastDiagnosticsBuild = DateTime.MinValue;
    private List<string> _cachedDiagnostics = [];
    private List<SensorAvailabilityDiagnostic> _cachedSensorAvailabilityDiagnostics = [];

    // Estatísticas por série da sessão (min/max/avg; zeradas pelo usuário).
    private readonly SeriesStatsTracker _seriesStats = new();

    // Sensores customizados (fórmulas do usuário): avaliados no fim de cada snapshot.
    private readonly CustomSensorEngine? _customSensors;

    // Ajuste por série (P9.1): cadência de amostragem + suavização antes das estatísticas.
    private readonly SensorTuningEngine? _sensorTuning;

    public event Action<SensorData>? SensorUpdated;
    public bool IsRunning { get; private set; }

    /// <summary>Pausa (P6.7): os ciclos pulam sem publicar evento; último snapshot fica.</summary>
    public bool Paused { get; private set; }

    public void SetPaused(bool paused) => Paused = paused;

    public LinuxSensorService(int updateIntervalMs = 1000, CustomSensorEngine? customSensors = null, SensorTuningEngine? sensorTuning = null)
    {
        _updateIntervalMs = updateIntervalMs;
        _hardwarePowerReader = new LinuxHardwarePowerReader(_gpuSensorReader);
        _customSensors = customSensors;
        _sensorTuning = sensorTuning;
        _timer = new Timer(_ => Update(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        IsRunning = true;
        _fpsMonitor.Start();
        _timer.Change(0, _updateIntervalMs);
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _fpsMonitor.Stop();
        IsRunning = false;
    }

    public SensorData GetCurrentSnapshot() => _current;

    /// <summary>Zera as estatísticas por série da sessão (bandeja/inventário).</summary>
    public void ResetSeriesStats() => _seriesStats.Reset();

    private void Update()
    {
        // Pausa (P6.7): sem novo evento; consumidores congelam no último snapshot.
        if (Paused) return;

        // Um tick demorado (leitura de /sys lenta em RAID/NFS) não pode se sobrepor ao
        // próximo: corrompia os contadores _prevCpu*/_prevDisk* (uso sugerindo ~100%).
        if (!Monitor.TryEnter(_updateLock)) return;
        try
        {
            var data = new SensorData
            {
                Timestamp = DateTime.UtcNow
            };

            if ((data.Timestamp - _lastHardwareIdentityRefresh).TotalSeconds >= 10)
            {
                _hardwareIdentities = _hardwareIdentityReader.Read();
                _lastHardwareIdentityRefresh = data.Timestamp;
            }
            data.HardwareDevices = _hardwareIdentities;

            _gpuSensorReader.BeginSample(data.Timestamp);
            _cpuMemoryReader.ReadCpuStats(data);
            _cpuMemoryReader.ReadMemInfo(data);
            _diskNetworkReader.ReadNetworkStats(data);
            _hardwarePowerReader.ReadHwmonSensors(data);
            _diskNetworkReader.ReadDiskStats(data);
            _gpuSensorReader.ReadDeviceMetrics(data);
            _hardwarePowerReader.ReadPowerSupply(data);

            if (_fpsMonitor.HasData)
            {
                data.Values[SensorType.Fps] = _fpsMonitor.Fps;
                data.Values[SensorType.FrametimeMs] = _fpsMonitor.FrametimeMs;
                data.Values[SensorType.FpsAvg] = _fpsMonitor.FpsAvg;
                data.Values[SensorType.FpsMin] = _fpsMonitor.FpsMin;
                data.Values[SensorType.Fps1Low] = _fpsMonitor.Fps1Low;
                data.Values[SensorType.Fps01Low] = _fpsMonitor.Fps01Low;
                data.Values[SensorType.FpsMax] = _fpsMonitor.FpsMax;
                data.FrametimeHistory = _fpsMonitor.FrametimeHistory.ToArray();
            }

            // Sensores customizados avaliam ANTES do commit das estatísticas:
            // min/máx/média da sessão, CSV/SDK/HTTP e inventário enxergam como nativas.
            _customSensors?.Evaluate(data);

            // Ajuste por série (P9.1): cadência + suavização; repetidas pulam as
            // estatísticas (mesma semântica do serviço Windows).
            var repeated = _sensorTuning?.Apply(data);

            // Publicação: pré-computa as chaves de série do snapshot. Aqui não há mais
            // escrita em Values/Series (os passos abaixo só anexam estatísticas e
            // diagnóstico), então todos os consumidores por tick compartilham a mesma
            // lista (cada um reconstruía e reordenava a própria antes).
            data.CachedSeriesKeys = data.GetSeriesKeys();

            // Estatísticas por série: uma amostra por snapshot publicado.
            _seriesStats.Commit(data, repeated);
            data.SeriesStats = _seriesStats.CopySnapshot();

            // Diagnóstico de fontes (Sistema/DISCO/GPU...) re-enumera sysfs/DRM:
            // no máximo 1x/s. As listas mudam devagar; a cada tick isso custava
            // dezenas de I/O e alocações no ritmo mínimo de 10 Hz. Consumidores
            // recebem a lista estável (a reconstrução usa uma lista nova antes
            // de publicar) — ver docs diagnostico.
            if ((data.Timestamp - _lastDiagnosticsBuild) >= DiagnosticsRefreshInterval)
            {
                _lastDiagnosticsBuild = data.Timestamp;
                _cachedDiagnostics = BuildSensorDiagnostics(data);
                _cachedSensorAvailabilityDiagnostics = data.SensorAvailabilityDiagnostics;
            }
            data.SensorDebugInfo = _cachedDiagnostics;
            data.SensorAvailabilityDiagnostics = _cachedSensorAvailabilityDiagnostics;

            _current = data;
            if (IsRunning)
                SensorUpdated?.Invoke(data); // callbacks pendentes do timer não sobrevivem ao Stop
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Update", ex);
        }
        finally
        {
            Monitor.Exit(_updateLock);
        }
    }

    /// <summary>
    /// Lê a utilização da CPU a partir de /proc/stat
    /// </summary>
    private List<string> BuildSensorDiagnostics(SensorData data)
    {
        // Disponibilidade por categoria resolve em UMA passada sobre as séries:
        // antes, cada AddAvailability varria data.Series inteira (O(tipos × séries)
        // por reconstrução).
        var availableTypes = new HashSet<SensorType>(data.Values.Keys);
        foreach (var seriesKey in data.Series.Keys)
            availableTypes.Add(seriesKey.Type);

        var lines = new List<string>
        {
            "[SISTEMA] Fontes Linux consultadas pelo Phantom Monitor; ausência de série pode indicar que a fonte não a publica."
        };

        AddAvailability(lines, availableTypes, "CPU", SensorType.CpuTotal, "/proc/stat", "arquivo ausente ou sem amostras válidas");
        AddAvailability(lines, availableTypes, "CPU", SensorType.CpuPerCore, "/proc/stat", "o kernel não publicou linhas por núcleo");
        AddAvailability(lines, availableTypes, "CPU", SensorType.CpuTemperature, "/sys/class/hwmon (coretemp/k10temp/zenpower)", "nenhum canal de temperatura da CPU foi exposto");
        AddAvailability(lines, availableTypes, "CPU", SensorType.CpuClock, "sysfs cpufreq", "frequência atual não publicada pelo driver de CPU");

        foreach (var type in new[] { SensorType.RamUsage, SensorType.RamUsed, SensorType.RamTotal, SensorType.RamAvailable })
            AddAvailability(lines, availableTypes, "MEMÓRIA", type, "/proc/meminfo", "campo necessário ausente ou inválido");

        foreach (var type in new[] { SensorType.DiskRead, SensorType.DiskWrite, SensorType.DiskUsage })
            AddAvailability(lines, availableTypes, "DISCO", type, "/proc/diskstats", "nenhum disco elegível publicou contadores");
        AddAvailability(lines, availableTypes, "DISCO", SensorType.DiskTemperature, "/sys/class/hwmon", "o dispositivo não expôs temperatura");

        foreach (var type in new[] { SensorType.NetworkDown, SensorType.NetworkUp })
            AddAvailability(lines, availableTypes, "REDE", type, "/proc/net/dev", "nenhuma interface publicou contadores");

        AddAvailability(lines, availableTypes, "VENTOINHAS", SensorType.FanSpeed, "/sys/class/hwmon", "nenhum fan*_input foi exposto");

        foreach (var type in new[] { SensorType.BatteryLevel, SensorType.BatteryVoltage, SensorType.BatteryPower,
                     SensorType.BatteryWearLevel, SensorType.BatteryAcPower })
            AddAvailability(lines, availableTypes, "BATERIA", type, "/sys/class/power_supply", "nenhuma bateria compatível detectada (normal em desktops)");

        foreach (var type in new[] { SensorType.Fps, SensorType.FrametimeMs, SensorType.FpsAvg, SensorType.FpsMin,
                     SensorType.Fps1Low, SensorType.Fps01Low, SensorType.FpsMax })
            AddAvailability(lines, availableTypes, "FPS", type, "memória compartilhada da PhantomVulkanLayer", _fpsMonitor.DebugInfo);
        lines.Add($"[FPS] Camada: {_fpsMonitor.DebugInfo}");

        _gpuSensorReader.AppendDiagnostics(lines, data);
        return lines;
    }

    private static void AddAvailability(
        ICollection<string> lines,
        HashSet<SensorType> availableTypes,
        string group,
        SensorType type,
        string source,
        string missingReason)
    {
        var available = availableTypes.Contains(type);
        lines.Add($"[{group}] {type}: {(available ? $"disponível via {source}" : $"indisponível — {missingReason}")}");
    }

    public void Dispose()
    {
        Stop();
        using var callbacksFinished = new ManualResetEvent(false);
        if (_timer.Dispose(callbacksFinished))
            callbacksFinished.WaitOne();

        _fpsMonitor.Dispose();
        _gpuSensorReader.Dispose();
    }
}
