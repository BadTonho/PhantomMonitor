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
public sealed class LinuxSensorService : ISensorService
{
    private readonly Timer _timer;
    private readonly int _updateIntervalMs;
    private readonly LinuxFpsMonitor _fpsMonitor = new();
    private readonly NvidiaGpuSensorReader _nvidiaGpuSensorReader = new();
    private readonly object _updateLock = new();
    private SensorData _current = new();

    // CPU calculation state
    private long _prevCpuTotal;
    private long _prevCpuIdle;
    private readonly Dictionary<int, (long Total, long Idle)> _prevPerCore = [];

    // Disk calculation state
    private long _prevDiskReadSectors;
    private long _prevDiskWriteSectors;
    private DateTime _prevDiskTimestamp = DateTime.UtcNow;
    private readonly Dictionary<string, int> _diskSlots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Read, long Write)> _prevPerDisk = new(StringComparer.Ordinal);

    /// <summary>Contagem de núcleos com cpufreq, detectada na primeira leitura
    /// (layout estável na sessão; ver ReadCpuStats).</summary>
    private int? _cpuCoreCount;

    // Network state (↑/↓ por interface + totais da sessão)
    private readonly Dictionary<string, int> _nicSlots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Rx, long Tx)> _prevPerNic = new(StringComparer.Ordinal);
    private DateTime _prevNetTimestamp = DateTime.MinValue;
    private DateTime? _lastNetSampleUtc;
    private double _netDownSessionBytes;
    private double _netUpSessionBytes;

    // Slot estável por caminho de hwmon (hwmon com o mesmo "name", como duas amdgpu).
    private readonly Dictionary<string, int> _hwmonSlots = new(StringComparer.Ordinal);

    // Diagnóstico de fontes: reconstruído no máximo 1x/s (re-enumera sysfs/DRM por
    // chamada; a cada tick multiplicava I/O em polling rápido). Lista reusada entre
    // snapshots até a próxima reconstrução — a reconstrução gera lista nova antes
    // de publicar, consumidor nunca vê lista parcial.
    private static readonly TimeSpan DiagnosticsRefreshInterval = TimeSpan.FromSeconds(1);
    private DateTime _lastDiagnosticsBuild = DateTime.MinValue;
    private List<string> _cachedDiagnostics = [];

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

            ReadCpuStats(data);
            ReadMemInfo(data);
            ReadNetworkStats(data);
            ReadHwmonSensors(data);
            ReadDiskStats(data);
            ReadGpuSysfs(data);
            ReadPowerSupply(data);

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
            }
            data.SensorDebugInfo = _cachedDiagnostics;

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
    private void ReadCpuStats(SensorData data)
    {
        try
        {
            if (!File.Exists("/proc/stat")) return;

            // Uma única passada: a leitura duplicada de /proc/stat achava o
            // agregado e depois relia o arquivo todo para os núcleos.
            string? cpuLine = null;
            var perCoreLines = new List<string>(16);
            foreach (var line in File.ReadLines("/proc/stat"))
            {
                if (cpuLine is null && line.StartsWith("cpu ", StringComparison.Ordinal))
                {
                    cpuLine = line;
                    continue;
                }
                if (line.StartsWith("cpu", StringComparison.Ordinal) && line.Length > 3)
                    perCoreLines.Add(line);
            }
            if (cpuLine is null) return;

            var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5)
            {
                long user = long.Parse(parts[1], CultureInfo.InvariantCulture);
                long nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
                long system = long.Parse(parts[3], CultureInfo.InvariantCulture);
                long idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
                long iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;
                long irq = parts.Length > 6 ? long.Parse(parts[6], CultureInfo.InvariantCulture) : 0;
                long softirq = parts.Length > 7 ? long.Parse(parts[7], CultureInfo.InvariantCulture) : 0;
                long steal = parts.Length > 8 ? long.Parse(parts[8], CultureInfo.InvariantCulture) : 0;

                long idleTotal = idle + iowait;
                long nonIdle = user + nice + system + irq + softirq + steal;
                long total = idleTotal + nonIdle;

                long deltaTotal = total - _prevCpuTotal;
                long deltaIdle = idleTotal - _prevCpuIdle;

                if (deltaTotal > 0)
                {
                    double cpuUsage = (1.0 - ((double)deltaIdle / deltaTotal)) * 100.0;
                    data.SetValue(SensorType.CpuTotal, Math.Clamp(cpuUsage, 0.0, 100.0), "CPU Total");
                }

                _prevCpuTotal = total;
                _prevCpuIdle = idleTotal;
            }

            // Uso por núcleo: linhas "cpuN" coletadas acima, com estado por núcleo.
            foreach (var line in perCoreLines)
            {
                var coreToken = line["cpu".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (coreToken.Length == 0 || !int.TryParse(coreToken[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var core))
                    continue;

                var coreParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (coreParts.Length < 5) continue;

                long cUser = long.Parse(coreParts[1], CultureInfo.InvariantCulture);
                long cNice = long.Parse(coreParts[2], CultureInfo.InvariantCulture);
                long cSystem = long.Parse(coreParts[3], CultureInfo.InvariantCulture);
                long cIdle = long.Parse(coreParts[4], CultureInfo.InvariantCulture);
                long cIowait = coreParts.Length > 5 ? long.Parse(coreParts[5], CultureInfo.InvariantCulture) : 0;
                long cIrq = coreParts.Length > 6 ? long.Parse(coreParts[6], CultureInfo.InvariantCulture) : 0;
                long cSoftirq = coreParts.Length > 7 ? long.Parse(coreParts[7], CultureInfo.InvariantCulture) : 0;
                long cSteal = coreParts.Length > 8 ? long.Parse(coreParts[8], CultureInfo.InvariantCulture) : 0;

                long cIdleTotal = cIdle + cIowait;
                long cNonIdle = cUser + cNice + cSystem + cIrq + cSoftirq + cSteal;
                long cTotal = cIdleTotal + cNonIdle;

                if (_prevPerCore.TryGetValue(core, out var prev))
                {
                    long dTotal = cTotal - prev.Total;
                    long dIdle = cIdleTotal - prev.Idle;
                    if (dTotal > 0)
                    {
                        double usage = Math.Clamp(100.0 * (1.0 - (double)dIdle / dTotal), 0, 100);
                        data.SetValue(new SensorKey(SensorType.CpuPerCore, core + 1), usage, $"CPU Core #{core}");
                    }
                }
                _prevPerCore[core] = (cTotal, cIdleTotal);
            }

            // Frequência por núcleo (scaling_cur_freq em kHz). O layout de núcleos
            // com cpufreq é estável na sessão: a contagem é detectada uma única vez
            // (antes: até 512 File.Exists/ReadAllText por tick = O(núcleos) syscalls
            // a cada snapshot). Semântica original mantida: pára no primeiro core
            // sem cpufreq.
            if (_cpuCoreCount is null)
            {
                var count = 0;
                while (count < 512 && File.Exists($"/sys/devices/system/cpu/cpu{count}/cpufreq/scaling_cur_freq"))
                    count++;
                _cpuCoreCount = count;
            }
            for (var core = 0; core < _cpuCoreCount.Value; core++)
            {
                var freqPath = $"/sys/devices/system/cpu/cpu{core}/cpufreq/scaling_cur_freq";
                if (!long.TryParse(File.ReadAllText(freqPath).Trim(), out var coreKhz)) continue;
                data.SetValue(new SensorKey(SensorType.CpuClock, core + 1), coreKhz / 1000.0, $"CPU Core #{core}");
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Cpu", ex);
        }
    }

    /// <summary>
    /// Lê a memória física a partir de /proc/meminfo
    /// </summary>
    private void ReadMemInfo(SensorData data)
    {
        try
        {
            if (!File.Exists("/proc/meminfo")) return;

            long totalKb = 0;
            long availKb = 0;

            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                {
                    totalKb = ParseMemInfoValue(line);
                }
                else if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                {
                    availKb = ParseMemInfoValue(line);
                }

                if (totalKb > 0 && availKb > 0) break;
            }

            if (totalKb > 0)
            {
                long usedKb = totalKb - availKb;
                double totalGb = totalKb / (1024.0 * 1024.0);
                double availGb = availKb / (1024.0 * 1024.0);
                double usedGb = usedKb / (1024.0 * 1024.0);
                double usagePct = ((double)usedKb / totalKb) * 100.0;

                data.SetValue(SensorType.RamTotal, totalGb, "RAM Total");
                data.SetValue(SensorType.RamUsed, usedGb, "RAM Used");
                data.SetValue(SensorType.RamAvailable, availGb, "RAM Available");
                data.SetValue(SensorType.RamUsage, Math.Clamp(usagePct, 0.0, 100.0), "RAM Usage");
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Memory", ex);
        }
    }

    private static long ParseMemInfoValue(string line)
    {
        var parts = line.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length > 1)
        {
            var numStr = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
                return val;
        }
        return 0;
    }

    /// <summary>
    /// Lê temperaturas e sensores através do subsistema hwmon (/sys/class/hwmon/)
    /// </summary>
    private void ReadHwmonSensors(SensorData data)
    {
        try
        {
            const string hwmonBase = "/sys/class/hwmon";
            if (!Directory.Exists(hwmonBase)) return;

            foreach (var dir in Directory.GetDirectories(hwmonBase))
            {
                // Por diretório: um hwmon sumindo entre a listagem e a leitura
                // (permission denied comum em alguns sensores) não pode abortar o restante.
                try
                {
                    string name = "";
                    var namePath = Path.Combine(dir, "name");
                    if (TryReadSysfsFile(namePath, out var nameRaw))
                    {
                        name = nameRaw.ToLowerInvariant();
                    }

                    // Slot estável por caminho: hwmon com o mesmo "name" (duas amdgpu,
                    // por exemplo) precisa de instâncias disjuntas.
                    if (!_hwmonSlots.TryGetValue(dir, out var hwmonSlot))
                        hwmonSlot = _hwmonSlots[dir] = _hwmonSlots.Count + 1;

                    bool isCpuHwmon = name.Contains("coretemp") || name.Contains("k10temp") || name.Contains("zenpower");
                    bool isGpuHwmon = name.Contains("amdgpu") || name.Contains("nouveau") || name.Contains("nvidia");
                    bool isFanController = !isCpuHwmon && !isGpuHwmon;
                    var hwLabel = name;

                    // Ler temperaturas do diretório (o número do arquivo dá a instância: temp1, temp2...)
                    int mbTempSlot = 0;
                    foreach (var tempFile in Directory.GetFiles(dir, "temp*_input"))
                    {
                        if (!TryReadSysfsFile(tempFile, out var tempRaw) ||
                            !double.TryParse(tempRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliDeg))
                            continue;

                        double deg = milliDeg / 1000.0;
                        int fileSlot = ParseSysfsIndex(tempFile, "temp");

                        if (isCpuHwmon)
                        {
                            data.SetValue(new SensorKey(SensorType.CpuTemperature, fileSlot > 0 ? fileSlot : hwmonSlot), deg, hwLabel);
                            if (data.CpuTemp <= 0 || deg > data.CpuTemp)
                                data.SetValue(SensorType.CpuTemperature, deg, "CPU Temp");
                        }
                        else if (isGpuHwmon)
                        {
                            // Rótulo do hwmon distingue os diodos (amdgpu/nouveau): "edge" =
                            // diodo principal, "junction"/"hotspot" = hot spot, "mem"/"vram" =
                            // junction da VRAM. Sem rótulo (NVIDIA legado), tudo vira a
                            // temperatura canônica da GPU.
                            TryReadSysfsFile(Path.Combine(dir, $"temp{fileSlot}_label"), out var labelRaw);
                            var tempLabel = labelRaw.ToLowerInvariant();
                            if (tempLabel.Contains("junction") || tempLabel.Contains("hotspot"))
                            {
                                data.SetValue(new SensorKey(SensorType.GpuHotSpot, hwmonSlot), deg, hwLabel);
                                var hotSpot = data.GetValue(SensorType.GpuHotSpot);
                                if (hotSpot <= 0 || deg > hotSpot)
                                    data.SetValue(SensorType.GpuHotSpot, deg, "Hot Spot");
                            }
                            else if (tempLabel.Contains("mem") || tempLabel.Contains("vram"))
                            {
                                data.SetValue(new SensorKey(SensorType.GpuVramTemperature, hwmonSlot), deg, hwLabel);
                                var vramTemp = data.GetValue(SensorType.GpuVramTemperature);
                                if (vramTemp <= 0 || deg > vramTemp)
                                    data.SetValue(SensorType.GpuVramTemperature, deg, "Temp VRAM");
                            }
                            else
                            {
                                data.SetValue(new SensorKey(SensorType.GpuTemperature, hwmonSlot), deg, hwLabel);
                                if (data.GpuTemp <= 0 || deg > data.GpuTemp)
                                    data.SetValue(SensorType.GpuTemperature, deg, "GPU Temp");
                            }
                        }
                        else if (isFanController)
                        {
                            // Controladores (it8620/nct6775/EC...): temperatura por slot + canônica máxima.
                            mbTempSlot++;
                            data.SetValue(new SensorKey(SensorType.MotherboardTemp, mbTempSlot), deg, hwLabel);
                            var mbTemp = data.GetValue(SensorType.MotherboardTemp);
                            if (deg > mbTemp)
                                data.SetValue(SensorType.MotherboardTemp, deg, hwLabel);
                        }
                    }

                    // Ventoinhas: fan1_input → FanSpeed#1 ... (GPU também publica GpuFan canônica)
                    foreach (var fanFile in Directory.GetFiles(dir, "fan*_input"))
                    {
                        if (!TryReadSysfsFile(fanFile, out var fanRaw) ||
                            !double.TryParse(fanRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var rpm))
                            continue;

                        int fanSlot = ParseSysfsIndex(fanFile, "fan");
                        if (fanSlot <= 0) fanSlot = hwmonSlot;

                        data.SetValue(new SensorKey(SensorType.FanSpeed, hwmonSlot * 100 + fanSlot), rpm, hwLabel);
                        data.SetValue(SensorType.FanSpeed, Math.Max(rpm, data.GetValue(SensorType.FanSpeed)), "Fan (sistema)");
                        if (isGpuHwmon)
                            data.SetValue(SensorType.GpuFan, rpm);
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("LinuxSensorService.HwmonEntry", ex);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Hwmon", ex);
        }
    }

    /// <summary>Extraí o número embutido num arquivo sysfs (temp1_input → 1; fan2_input → 2).</summary>
    private static int ParseSysfsIndex(string path, string prefix)
    {
        var file = Path.GetFileName(path);
        var withPrefix = file.StartsWith(prefix, StringComparison.Ordinal) ? file[prefix.Length..] : file;
        int end = withPrefix.IndexOf('_');
        return end > 0 && int.TryParse(withPrefix[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) ? idx : 0;
    }

    private static bool TryReadSysfsFile(string path, out string content)
    {
        try
        {
            content = File.ReadAllText(path).Trim();
            return true;
        }
        catch
        {
            content = "";
            return false;
        }
    }

    /// <summary>Número da primeira linha sysfs; false se ausente/invalido.</summary>
    private static bool TryReadSysfsDouble(string path, out double value)
    {
        value = 0;
        return TryReadSysfsFile(path, out var raw) &&
               double.TryParse(raw.Split('\n')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Bateria/fonte AC (P6.4) via /sys/class/power_supply: classes por `type` —
    /// "Mains" (AC) alimenta BatteryAcPower (arquivo `online`), "Battery" alimenta
    /// nível (`capacity`), tensão (`voltage_now`, µV), potência (`power_now`, µW; ou
    /// `current_now` × tensão) e desgaste ((1 − energy_full/energy_full_design) × 100,
    /// com charge_full como respatilha). O sinal é normalizado por `status`:
    /// positivo = consumindo da bateria, negativo = carregando (drivers divergem no
    /// sinal de current_now/power_now). Desktop sem power_supply não publica nada.
    /// </summary>
    private void ReadPowerSupply(SensorData data)
    {
        try
        {
            const string psBase = "/sys/class/power_supply";
            if (!Directory.Exists(psBase)) return;

            foreach (var dir in Directory.GetDirectories(psBase))
            {
                if (!TryReadSysfsFile(Path.Combine(dir, "type"), out var psType)) continue;

                if (psType.Equals("Mains", StringComparison.OrdinalIgnoreCase) ||
                    psType.Equals("UPS", StringComparison.OrdinalIgnoreCase))
                {
                    // Fonte AC: fica ativa durante toda a sessão (o desktop não tem);
                    // publica 1/0 sempre que um adaptador existir.
                    if (TryReadSysfsFile(Path.Combine(dir, "online"), out var onlineRaw))
                    {
                        data.SetValue(SensorType.BatteryAcPower, onlineRaw == "1" ? 1 : 0,
                            $"Carregador ({Path.GetFileName(dir)})");
                    }
                    continue;
                }

                if (!psType.Equals("Battery", StringComparison.OrdinalIgnoreCase)) continue;

                var label = $"Bateria ({Path.GetFileName(dir)})";

                if (TryReadSysfsDouble(Path.Combine(dir, "capacity"), out var cap) && cap > 0)
                    data.SetValue(SensorType.BatteryLevel, Math.Clamp(cap, 0, 100), label);

                if (TryReadSysfsDouble(Path.Combine(dir, "voltage_now"), out var microVolts) && microVolts > 0)
                    data.SetValue(SensorType.BatteryVoltage, microVolts / 1_000_000.0, label);

                // Potência: power_now direto; sem esse arquivo, current_now × tensão.
                double powerW = 0;
                if (TryReadSysfsDouble(Path.Combine(dir, "power_now"), out var microWatts))
                    powerW = microWatts / 1_000_000.0;
                else if (TryReadSysfsDouble(Path.Combine(dir, "current_now"), out var microAmps) && microVolts > 0)
                    powerW = microAmps * microVolts / 1e12; // µA × µV = pW

                if (powerW != 0 && TryReadSysfsFile(Path.Combine(dir, "status"), out var status))
                {
                    powerW = status.Equals("Charging", StringComparison.OrdinalIgnoreCase)
                        ? -Math.Abs(powerW)
                        : Math.Abs(powerW);
                }

                if (powerW != 0)
                    data.SetValue(SensorType.BatteryPower, powerW, label);

                // Desgaste: energia (µWh) ou carga (µAh) — ambas dão a mesma razão.
                if (TryReadSysfsDouble(Path.Combine(dir, "energy_full"), out var full) && full > 0)
                {
                    if (TryReadSysfsDouble(Path.Combine(dir, "energy_full_design"), out var design) && design > 0)
                        data.SetValue(SensorType.BatteryWearLevel, Math.Clamp((1 - full / design) * 100.0, 0, 100), label);
                }
                else if (TryReadSysfsDouble(Path.Combine(dir, "charge_full"), out var chFull) && chFull > 0 &&
                         TryReadSysfsDouble(Path.Combine(dir, "charge_full_design"), out var chDesign) && chDesign > 0)
                {
                    data.SetValue(SensorType.BatteryWearLevel, Math.Clamp((1 - chFull / chDesign) * 100.0, 0, 100), label);
                }

                break; // primeira bateria: notebooks têm uma; multi-bateria via instância futura
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.PowerSupply", ex);
        }
    }

    /// <summary>
    /// Lê estatísticas de leitura e escrita de disco a partir de /proc/diskstats
    /// </summary>
    private void ReadDiskStats(SensorData data)
    {
        try
        {
            if (!File.Exists("/proc/diskstats")) return;

            var now = DateTime.UtcNow;
            var elapsedSec = (now - _prevDiskTimestamp).TotalSeconds;

            long totalReadSectors = 0;
            long totalWriteSectors = 0;
            var devices = new List<string>();

            foreach (var line in File.ReadLines("/proc/diskstats"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // Coluna 2: nome do dispositivo (nvme0n1, sda, etc.)
                if (parts.Length < 10) continue;
                string devName = parts[2];
                // Filtrar apenas discos principais, ignorando partições (sda1) e loopback
                if (!IsPhysicalDisk(devName)) continue;
                if (!long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rSectors) ||
                    !long.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var wSectors))
                    continue;

                devices.Add(devName);
                totalReadSectors += rSectors;
                totalWriteSectors += wSectors;

                // Série por disco: slot estável por nome na ordem de enumeração.
                if (!_diskSlots.TryGetValue(devName, out var slot)) slot = _diskSlots[devName] = _diskSlots.Count + 1;

                if (elapsedSec > 0 && _prevPerDisk.TryGetValue(devName, out var prev))
                {
                    double readBps = Math.Max(0, rSectors - prev.Read) * 512.0 / elapsedSec;
                    double writeBps = Math.Max(0, wSectors - prev.Write) * 512.0 / elapsedSec;
                    data.SetValue(new SensorKey(SensorType.DiskRead, slot), readBps, devName);
                    data.SetValue(new SensorKey(SensorType.DiskWrite, slot), writeBps, devName);
                }
                _prevPerDisk[devName] = (rSectors, wSectors);
            }

            var elapsed = (now - _prevDiskTimestamp).TotalSeconds;
            if (elapsed > 0 && _prevDiskReadSectors > 0)
            {
                long deltaRead = Math.Max(0, totalReadSectors - _prevDiskReadSectors);
                long deltaWrite = Math.Max(0, totalWriteSectors - _prevDiskWriteSectors);

                // 1 setor = 512 bytes
                double readBytesSec = (deltaRead * 512.0) / elapsed;
                double writeBytesSec = (deltaWrite * 512.0) / elapsed;

                data.SetValue(SensorType.DiskRead, readBytesSec, "Discos (somatório)");
                data.SetValue(SensorType.DiskWrite, writeBytesSec, "Discos (somatório)");

                // Estimativa de atividade de disco
                double totalThroughputMB = (readBytesSec + writeBytesSec) / (1024.0 * 1024.0);
                double usage = Math.Min(100.0, (totalThroughputMB / 500.0) * 100.0);
                data.SetValue(SensorType.DiskUsage, usage, "Discos (estimado)");
            }

            _prevDiskReadSectors = totalReadSectors;
            _prevDiskWriteSectors = totalWriteSectors;
            _prevDiskTimestamp = now;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Disk", ex);
        }
    }

    /// <summary>Discos físicos principais (sda..sdz, nvme0n1); ignora partições/loop/ram/usb flash?</summary>
    private static bool IsPhysicalDisk(string devName) =>
        (devName.StartsWith("sd", StringComparison.Ordinal) && devName.Length == 3 && char.IsAsciiLetter(devName[2])) ||
        (devName.StartsWith("nvme", StringComparison.Ordinal) && devName.Length > 4 && char.IsAsciiDigit(devName[4]) && devName.Contains('n') && !devName.Contains('p'));

    /// <summary>
    /// Lê tráfego de rede por interface a partir de /proc/net/dev (↑/↓ + totais da sessão).
    /// </summary>
    private void ReadNetworkStats(SensorData data)
    {
        try
        {
            const string netDevPath = "/proc/net/dev";
            if (!File.Exists(netDevPath)) return;

            var now = DateTime.UtcNow;
            if (_prevNetTimestamp == DateTime.MinValue) _prevNetTimestamp = now;
            var elapsedSec = (now - _prevNetTimestamp).TotalSeconds;

            var interfaces = new List<(string Name, long Rx, long Tx)>();
            foreach (var line in File.ReadLines(netDevPath))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;

                var ifName = line[..colon].Trim();
                if (ifName == "lo") continue; // loopback polui totais sem tráfego real de NIC

                var fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 9) continue;
                if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rx) ||
                    !long.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tx))
                    continue;

                interfaces.Add((ifName, rx, tx));
            }
            _prevNetTimestamp = now;
            if (interfaces.Count == 0) return;

            double downBps = 0;
            double upBps = 0;
            foreach (var (name, rx, tx) in interfaces)
            {
                if (!_nicSlots.TryGetValue(name, out var slot)) slot = _nicSlots[name] = _nicSlots.Count + 1;

                if (elapsedSec > 0 && _prevPerNic.TryGetValue(name, out var prev))
                {
                    double rxBps = Math.Max(0, rx - prev.Rx) / elapsedSec;
                    double txBps = Math.Max(0, tx - prev.Tx) / elapsedSec;
                    downBps += rxBps;
                    upBps += txBps;
                    data.SetValue(new SensorKey(SensorType.NetworkDown, slot), rxBps, name);
                    data.SetValue(new SensorKey(SensorType.NetworkUp, slot), txBps, name);
                }
                _prevPerNic[name] = (rx, tx);
            }

            if (downBps > 0 || upBps > 0)
            {
                data.SetValue(SensorType.NetworkDown, downBps, "Rede ↓ (todas as interfaces)");
                data.SetValue(SensorType.NetworkUp, upBps, "Rede ↑ (todas as interfaces)");

                // Totais da sessão: velocidade observada x tempo desde a amostra anterior.
                if (_lastNetSampleUtc is { } last)
                {
                    var dt = (now - last).TotalSeconds;
                    _netDownSessionBytes += downBps * dt;
                    _netUpSessionBytes += upBps * dt;
                }
                _lastNetSampleUtc = now;

                data.SetValue(SensorType.NetworkDownTotal, _netDownSessionBytes, "Total recebido (sessão)");
                data.SetValue(SensorType.NetworkUpTotal, _netUpSessionBytes, "Total enviado (sessão)");
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Network", ex);
        }
    }

    /// <summary>
    /// Lê a utilização de GPU via subsistema DRM (/sys/class/drm) — com séries por card
    /// (multi-GPU: GpuCore#1 = card com menor índice).
    /// </summary>
    private void ReadGpuSysfs(SensorData data)
    {
        try
        {
            _nvidiaGpuSensorReader.BeginSample();
            if (!Directory.Exists("/sys/class/drm")) return;

            var cards = Directory.GetDirectories("/sys/class/drm")
                .Select(d => Path.GetFileName(d))
                .Where(n => n.StartsWith("card", StringComparison.Ordinal) &&
                            n[4..].All(char.IsAsciiDigit))
                .OrderBy(n => int.Parse(n[4..], CultureInfo.InvariantCulture))
                .ToArray();

            var cardIndex = 0;
            var nvidiaIndex = 0u;
            foreach (var cardName in cards)
            {
                cardIndex++;
                var deviceDir = Path.Combine("/sys/class/drm", cardName, "device");

                var isPrimary = cardIndex == 1;

                // O driver amdgpu publica utilização/VRAM no sysfs DRM. O driver
                // NVIDIA publica essas leituras pela NVML, carregada opcionalmente.
                if (TryReadSysfsFile(Path.Combine(deviceDir, "vendor"), out var vendorRaw) &&
                    vendorRaw.Equals("0x10de", StringComparison.OrdinalIgnoreCase))
                {
                    var nvmlIndex = nvidiaIndex++;
                    if (_nvidiaGpuSensorReader.TryRead(ReadPciBusId(deviceDir), nvmlIndex, out var nvidia))
                        PublishNvidiaGpuReading(data, cardIndex, nvidia);
                }

                // AMD GPU Busy Percent
                var busyPath = Path.Combine(deviceDir, "gpu_busy_percent");
                if (File.Exists(busyPath) &&
                    double.TryParse(File.ReadAllText(busyPath).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var busy))
                {
                    data.SetValue(new SensorKey(SensorType.GpuCore, cardIndex), Math.Clamp(busy, 0.0, 100.0), cardName);
                    if (isPrimary || !data.Values.ContainsKey(SensorType.GpuCore))
                        data.SetValue(SensorType.GpuCore, Math.Clamp(busy, 0.0, 100.0), "GPU Core");
                }

                // AMD VRAM usada/total (MB)
                var vramUsedPath = Path.Combine(deviceDir, "mem_info_vram_used");
                if (File.Exists(vramUsedPath) &&
                    long.TryParse(File.ReadAllText(vramUsedPath).Trim(), out var bytes))
                {
                    data.SetValue(new SensorKey(SensorType.GpuMemoryUsed, cardIndex), bytes / (1024.0 * 1024.0), cardName); // MB
                    if (isPrimary || !data.Values.ContainsKey(SensorType.GpuMemoryUsed))
                        data.SetValue(SensorType.GpuMemoryUsed, bytes / (1024.0 * 1024.0), "VRAM Usada"); // MB
                }

                var vramTotalPath = Path.Combine(deviceDir, "mem_info_vram_total");
                if (File.Exists(vramTotalPath) &&
                    long.TryParse(File.ReadAllText(vramTotalPath).Trim(), out var totalBytes))
                {
                    data.SetValue(new SensorKey(SensorType.GpuMemoryTotal, cardIndex), totalBytes / (1024.0 * 1024.0), cardName); // MB
                    if (isPrimary || !data.Values.ContainsKey(SensorType.GpuMemoryTotal))
                        data.SetValue(SensorType.GpuMemoryTotal, totalBytes / (1024.0 * 1024.0), "VRAM Total"); // MB
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.Gpu", ex);
        }
    }

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
        AddAvailability(lines, availableTypes, "GPU", SensorType.GpuFan, "hwmon da GPU", "nenhum fan*_input da GPU foi exposto");

        foreach (var type in new[] { SensorType.BatteryLevel, SensorType.BatteryVoltage, SensorType.BatteryPower,
                     SensorType.BatteryWearLevel, SensorType.BatteryAcPower })
            AddAvailability(lines, availableTypes, "BATERIA", type, "/sys/class/power_supply", "nenhuma bateria compatível detectada (normal em desktops)");

        foreach (var type in new[] { SensorType.Fps, SensorType.FrametimeMs, SensorType.FpsAvg, SensorType.FpsMin,
                     SensorType.Fps1Low, SensorType.Fps01Low, SensorType.FpsMax })
            AddAvailability(lines, availableTypes, "FPS", type, "memória compartilhada da PhantomVulkanLayer", _fpsMonitor.DebugInfo);
        lines.Add($"[FPS] Camada: {_fpsMonitor.DebugInfo}");

        AppendGpuSourceDiagnostics(lines, data);
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

    private void AppendGpuSourceDiagnostics(ICollection<string> lines, SensorData data)
    {
        var gpuCards = new List<(string Card, string Vendor, string Driver)>();
        var metricSources = new Dictionary<SensorType, HashSet<string>>();

        void AddMetricSource(SensorType type, string source)
        {
            if (!metricSources.TryGetValue(type, out var sources))
                metricSources[type] = sources = new HashSet<string>(StringComparer.Ordinal);
            sources.Add(source);
        }

        bool HasMetric(SensorType type) =>
            data.Values.ContainsKey(type) || data.Series.Keys.Any(key => key.Type == type);

        try
        {
            if (Directory.Exists("/sys/class/drm"))
            {
                foreach (var path in Directory.GetDirectories("/sys/class/drm")
                             .Where(path => Path.GetFileName(path).StartsWith("card", StringComparison.Ordinal) &&
                                           Path.GetFileName(path)[4..].All(char.IsAsciiDigit)))
                {
                    var card = Path.GetFileName(path);
                    var device = Path.Combine(path, "device");
                    _ = TryReadSysfsFile(Path.Combine(device, "vendor"), out var vendor);
                    var driver = "não identificado";
                    try
                    {
                        var target = new DirectoryInfo(Path.Combine(device, "driver")).ResolveLinkTarget(returnFinalTarget: true);
                        if (target is not null) driver = target.Name;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    gpuCards.Add((card, vendor, driver));
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.GpuDiagnostics", ex);
            lines.Add($"[GPU] Não foi possível enumerar dispositivos DRM: {ex.Message}");
        }

        if (gpuCards.Count == 0)
            lines.Add("[GPU] Nenhuma placa gráfica foi detectada em /sys/class/drm.");
        foreach (var (card, vendor, driver) in gpuCards)
            lines.Add($"[GPU] DRM {card}: vendor={vendor}; driver={driver}.");

        var nvidiaPresent = gpuCards.Any(card => card.Vendor.Equals("0x10de", StringComparison.OrdinalIgnoreCase));
        if (nvidiaPresent)
        {
            lines.Add($"[GPU] NVML: {_nvidiaGpuSensorReader.Status}");
            if (HasMetric(SensorType.GpuCore) && _nvidiaGpuSensorReader.LastUtilizationAvailable)
                AddMetricSource(SensorType.GpuCore, "NVML");
            if (HasMetric(SensorType.GpuMemoryUsed) && _nvidiaGpuSensorReader.LastMemoryAvailable)
                AddMetricSource(SensorType.GpuMemoryUsed, "NVML");
            if (HasMetric(SensorType.GpuMemoryTotal) && _nvidiaGpuSensorReader.LastMemoryAvailable)
                AddMetricSource(SensorType.GpuMemoryTotal, "NVML");
            if (HasMetric(SensorType.GpuTemperature) && _nvidiaGpuSensorReader.LastTemperatureAvailable)
                AddMetricSource(SensorType.GpuTemperature, "NVML");
        }

        foreach (var (card, vendor, _) in gpuCards)
        {
            var device = Path.Combine("/sys/class/drm", card, "device");
            var drmSource = $"DRM {card}";
            if (HasMetric(SensorType.GpuCore) && File.Exists(Path.Combine(device, "gpu_busy_percent")))
                AddMetricSource(SensorType.GpuCore, $"{drmSource}/gpu_busy_percent");
            if (HasMetric(SensorType.GpuMemoryUsed) && File.Exists(Path.Combine(device, "mem_info_vram_used")))
                AddMetricSource(SensorType.GpuMemoryUsed, $"{drmSource}/mem_info_vram_used");
            if (HasMetric(SensorType.GpuMemoryTotal) && File.Exists(Path.Combine(device, "mem_info_vram_total")))
                AddMetricSource(SensorType.GpuMemoryTotal, $"{drmSource}/mem_info_vram_total");
        }

        var gpuHwmonNames = new List<string>();
        try
        {
            if (Directory.Exists("/sys/class/hwmon"))
            {
                foreach (var dir in Directory.GetDirectories("/sys/class/hwmon"))
                {
                    if (!TryReadSysfsFile(Path.Combine(dir, "name"), out var name)) continue;
                    var normalized = name.ToLowerInvariant();
                    if (!normalized.Contains("amdgpu") && !normalized.Contains("nouveau") && !normalized.Contains("nvidia"))
                        continue;

                    var channels = new List<string>();
                    foreach (var tempFile in Directory.GetFiles(dir, "temp*_input"))
                    {
                        if (!TryReadSysfsDouble(tempFile, out _)) continue;
                        var fileSlot = ParseSysfsIndex(tempFile, "temp");
                        _ = TryReadSysfsFile(Path.Combine(dir, $"temp{fileSlot}_label"), out var label);
                        var normalizedLabel = label.ToLowerInvariant();
                        var source = $"hwmon({name})/{Path.GetFileName(tempFile)}";
                        if (normalizedLabel.Contains("junction") || normalizedLabel.Contains("hotspot"))
                        {
                            channels.Add($"{Path.GetFileName(tempFile)} ({label}; hotspot)");
                            if (HasMetric(SensorType.GpuHotSpot)) AddMetricSource(SensorType.GpuHotSpot, source);
                        }
                        else if (normalizedLabel.Contains("mem") || normalizedLabel.Contains("vram"))
                        {
                            channels.Add($"{Path.GetFileName(tempFile)} ({label}; VRAM)");
                            if (HasMetric(SensorType.GpuVramTemperature)) AddMetricSource(SensorType.GpuVramTemperature, source);
                        }
                        else
                        {
                            channels.Add($"{Path.GetFileName(tempFile)} ({(string.IsNullOrWhiteSpace(label) ? "GPU" : label)})");
                            if (HasMetric(SensorType.GpuTemperature)) AddMetricSource(SensorType.GpuTemperature, source);
                        }
                    }

                    foreach (var fanFile in Directory.GetFiles(dir, "fan*_input"))
                    {
                        if (!TryReadSysfsDouble(fanFile, out _)) continue;
                        channels.Add(Path.GetFileName(fanFile));
                        if (HasMetric(SensorType.GpuFan))
                            AddMetricSource(SensorType.GpuFan, $"hwmon({name})/{Path.GetFileName(fanFile)}");
                    }

                    gpuHwmonNames.Add(channels.Count == 0 ? name : $"{name} [{string.Join(", ", channels)}]");
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxSensorService.GpuHwmonDiagnostics", ex);
        }

        lines.Add(gpuHwmonNames.Count == 0
            ? "[GPU] hwmon: nenhum sensor de GPU (amdgpu/nouveau/nvidia) foi publicado."
            : $"[GPU] hwmon detectado: {string.Join(", ", gpuHwmonNames)}.");

        var nvidiaDriver = gpuCards.FirstOrDefault(card => card.Vendor.Equals("0x10de", StringComparison.OrdinalIgnoreCase)).Driver;
        var sourceMissing = nvidiaPresent
            ? $"NVML: {_nvidiaGpuSensorReader.Status}; driver NVIDIA {nvidiaDriver} não publicou alternativa DRM/hwmon compatível"
            : "nenhuma fonte GPU compatível publicou esta métrica";

        AddGpuMetricStatus(lines, data, metricSources, SensorType.GpuCore, sourceMissing);
        AddGpuMetricStatus(lines, data, metricSources, SensorType.GpuMemoryUsed, sourceMissing);
        AddGpuMetricStatus(lines, data, metricSources, SensorType.GpuMemoryTotal, sourceMissing);
        AddGpuMetricStatus(lines, data, metricSources, SensorType.GpuTemperature, "NVML/hwmon não publicou temperatura principal");
        AddGpuMetricStatus(lines, data, metricSources, SensorType.GpuHotSpot, "hwmon não publicou canal com rótulo hotspot/junction");
        AddGpuMetricStatus(lines, data, metricSources, SensorType.GpuVramTemperature, "hwmon não publicou canal com rótulo mem/vram");
        AddGpuMetricStatus(lines, data, metricSources, SensorType.GpuFan, "hwmon não publicou fan*_input para a GPU");
    }

    private static void AddGpuMetricStatus(
        ICollection<string> lines,
        SensorData data,
        IReadOnlyDictionary<SensorType, HashSet<string>> metricSources,
        SensorType type,
        string missingReason)
    {
        var available = data.Values.ContainsKey(type) || data.Series.Keys.Any(key => key.Type == type);
        var sources = metricSources.TryGetValue(type, out var reportedBy)
            ? string.Join(", ", reportedBy.OrderBy(source => source, StringComparer.Ordinal))
            : "origem não identificada";
        lines.Add($"[GPU] {type}: {(available ? $"disponível nesta amostra; fonte(s): {sources}" : $"indisponível — {missingReason}")}");
    }

    private static string? ReadPciBusId(string deviceDir)
    {
        if (TryReadSysfsFile(Path.Combine(deviceDir, "uevent"), out var uevent))
        {
            var slot = uevent.Split('\n')
                .FirstOrDefault(line => line.StartsWith("PCI_SLOT_NAME=", StringComparison.Ordinal));
            if (slot is not null)
                return slot["PCI_SLOT_NAME=".Length..].Trim();
        }

        try
        {
            var target = new DirectoryInfo(deviceDir).ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? null : Path.GetFileName(target.FullName);
        }
        catch
        {
            return null;
        }
    }

    private static void PublishNvidiaGpuReading(SensorData data, int cardIndex, NvidiaGpuReading reading)
    {
        if (reading.UtilizationPercent is double utilization)
        {
            data.SetValue(new SensorKey(SensorType.GpuCore, cardIndex), utilization, reading.Label);
            if (!data.Values.ContainsKey(SensorType.GpuCore))
                data.SetValue(SensorType.GpuCore, utilization, reading.Label);
        }

        if (reading.MemoryUsedMb is double memoryUsed)
        {
            data.SetValue(new SensorKey(SensorType.GpuMemoryUsed, cardIndex), memoryUsed, reading.Label);
            if (!data.Values.ContainsKey(SensorType.GpuMemoryUsed))
                data.SetValue(SensorType.GpuMemoryUsed, memoryUsed, reading.Label);
        }

        if (reading.MemoryTotalMb is double memoryTotal)
        {
            data.SetValue(new SensorKey(SensorType.GpuMemoryTotal, cardIndex), memoryTotal, reading.Label);
            if (!data.Values.ContainsKey(SensorType.GpuMemoryTotal))
                data.SetValue(SensorType.GpuMemoryTotal, memoryTotal, reading.Label);
        }

        if (reading.TemperatureCelsius is double temperature)
        {
            data.SetValue(new SensorKey(SensorType.GpuTemperature, cardIndex), temperature, reading.Label);
            if (!data.Values.TryGetValue(SensorType.GpuTemperature, out var currentTemperature) ||
                temperature > currentTemperature)
            {
                data.SetValue(SensorType.GpuTemperature, temperature, reading.Label);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        using var callbacksFinished = new ManualResetEvent(false);
        if (_timer.Dispose(callbacksFinished))
            callbacksFinished.WaitOne();

        _fpsMonitor.Dispose();
        _nvidiaGpuSensorReader.Dispose();
    }
}
