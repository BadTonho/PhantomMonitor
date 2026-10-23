using System.Globalization;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.Services.Linux;

internal sealed class LinuxHardwarePowerReader
{

    private readonly Dictionary<string, int> _hwmonSlots = new(StringComparer.Ordinal);
    private readonly LinuxGpuSensorReader _gpuSensorReader;

    public LinuxHardwarePowerReader(LinuxGpuSensorReader gpuSensorReader) => _gpuSensorReader = gpuSensorReader;
    internal void ReadHwmonSensors(SensorData data)
    {
        try
        {
            const string hwmonBase = "/sys/class/hwmon";
            if (!Directory.Exists(hwmonBase)) return;

            foreach (var dir in Directory.GetDirectories(hwmonBase))
            {
                // Por diretório: um hwmon sumindo entre a listagem e a leitura
                // (permission denied comum em alguns sensores) não pode abortar o restante.
                var name = "";
                var gpuCardIndex = 0;
                var gpuLabel = "";
                var isGpuHwmon = false;
                try
                {
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
                    isGpuHwmon = _gpuSensorReader.IsGpuHwmonDirectory(dir, out gpuCardIndex, out gpuLabel);
                    bool isFanController = !isCpuHwmon && !isGpuHwmon;
                    var hwLabel = name;

                    // Ler temperaturas do diretório (o número do arquivo dá a instância: temp1, temp2...)
                    int mbTempSlot = 0;
                    foreach (var tempFile in Directory.GetFiles(dir, "temp*_input"))
                    {
                        int fileSlot = ParseSysfsIndex(tempFile, "temp");
                        if (isGpuHwmon)
                        {
                            _gpuSensorReader.ReadHwmonTemperature(data, dir, gpuCardIndex, name, gpuLabel,
                                tempFile, fileSlot);
                            continue;
                        }

                        if (!TryReadSysfsFile(tempFile, out var tempRaw) ||
                            !double.TryParse(tempRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliDeg))
                            continue;

                        double deg = milliDeg / 1000.0;

                        if (isCpuHwmon)
                        {
                            data.SetValue(new SensorKey(SensorType.CpuTemperature, fileSlot > 0 ? fileSlot : hwmonSlot), deg, hwLabel);
                            if (data.CpuTemp <= 0 || deg > data.CpuTemp)
                                data.SetValue(SensorType.CpuTemperature, deg, "CPU Temp");
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
                            _gpuSensorReader.PublishGpuFan(data, gpuCardIndex, rpm, gpuLabel,
                                $"hwmon({name})/{Path.GetFileName(fanFile)}");
                    }
                }
                catch (Exception ex)
                {
                    if (isGpuHwmon && ex is (IOException or UnauthorizedAccessException))
                        _gpuSensorReader.ReportHwmonDirectoryIssue(gpuCardIndex, dir, ex.GetType().Name);
                    else
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

    internal void ReadPowerSupply(SensorData data)
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

}
