using System.Text.Json.Serialization;

namespace PhantomMonitor.Models;

public sealed class SensorData
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Séries canônicas (índice 0): compatíveis com as versões anteriores do formato JSON.
    /// Séries por instância vivem em <see cref="Series"/> com chaves <see cref="SensorKey"/>.
    /// </summary>
    public Dictionary<SensorType, double> Values { get; set; } = [];
    public Dictionary<SensorType, string> Labels { get; set; } = [];

    /// <summary>
    /// Séries por instância (multi-GPU, núcleos, ventoinhas, discos, rails...),
    /// chaveadas por <see cref="SensorKey"/> no formato "Categoria#índice".
    /// </summary>
    public Dictionary<SensorKey, double> Series { get; set; } = [];
    public Dictionary<SensorKey, string> SeriesLabels { get; set; } = [];

    /// <summary>
    /// Estatísticas por série acumuladas na sessão (min/max/avg; ver SeriesStatsTracker).
    /// Vai para o snapshot JSON; o CSV e a SDK continuam publicando só o valor atual.
    /// </summary>
    public Dictionary<SensorKey, SeriesStats> SeriesStats { get; set; } = [];

    /// <summary>
    /// Identidade dos monitores conectados (EDID via WMI, cache de ~60 s):
    /// vai para o snapshot JSON ("monitors") e para a aba "Monitores" do
    /// inventário; CSV/SDK não mudam (são séries numéricas). Ver MonitorInfo.
    /// </summary>
    public List<MonitorInfo> Monitors { get; set; } = [];

    [JsonIgnore]
    public List<GpuProcessInfo> GpuProcesses { get; set; } = [];
    [JsonIgnore]
    public List<string> SensorDebugInfo { get; set; } = [];

    public double GetValue(SensorType type) =>
        Values.TryGetValue(type, out var v) ? v : 0;

    public double GetValue(SensorKey key) =>
        key.IsPrimary
            ? GetValue(key.Type)
            : Series.TryGetValue(key, out var v) ? v : 0;

    public void SetValue(SensorType type, double value, string? label = null)
    {
        Values[type] = value;
        if (!string.IsNullOrEmpty(label))
        {
            Labels[type] = label;
        }
    }

    public void SetValue(SensorKey key, double value, string? label = null)
    {
        if (key.IsPrimary)
        {
            SetValue(key.Type, value, label);
            return;
        }

        Series[key] = value;
        if (!string.IsNullOrEmpty(label))
        {
            SeriesLabels[key] = label;
        }
    }

    public string GetLabel(SensorKey key)
    {
        if (key.IsPrimary && Labels.TryGetValue(key.Type, out var label)) return label;
        if (SeriesLabels.TryGetValue(key, out var seriesLabel)) return seriesLabel;
        return key.ToString();
    }

    /// <summary>
    /// Todas as séries presentes no snapshot: canônicas primeiro (na ordem do enum,
    /// compatível com o CSV antigo), depois instâncias ordenadas por chave.
    /// </summary>
    public List<SensorKey> GetSeriesKeys()
    {
        var keys = new List<SensorKey>(Values.Count + Series.Count);
        foreach (var type in Enum.GetValues<SensorType>())
        {
            if (Values.ContainsKey(type)) keys.Add(new SensorKey(type, 0));
        }

        var extras = Series.Keys.ToList();
        extras.Sort();
        keys.AddRange(extras);
        return keys;
    }

    public double CpuUsage => GetValue(SensorType.CpuTotal);
    public double CpuTemp => GetValue(SensorType.CpuTemperature);
    public double GpuUsage => GetValue(SensorType.GpuCore);
    public double GpuTemp => GetValue(SensorType.GpuTemperature);
    public double RamUsagePercent => GetValue(SensorType.RamUsage);
    public double RamAvailableGB => GetValue(SensorType.RamAvailable);

    // FPS
    public double Fps => GetValue(SensorType.Fps);
    public double FrametimeMs => GetValue(SensorType.FrametimeMs);
    public double FpsAvg => GetValue(SensorType.FpsAvg);
    public double FpsMin => GetValue(SensorType.FpsMin);
    public double Fps1Low => GetValue(SensorType.Fps1Low);
    public double Fps01Low => GetValue(SensorType.Fps01Low);
    public double FpsMax => GetValue(SensorType.FpsMax);
    [JsonIgnore]
    public float[] FrametimeHistory { get; set; } = [];
}

public sealed class GpuProcessInfo
{
    public string Name { get; set; } = "";
    public double Usage { get; set; }
    public ulong MemoryMb { get; set; }
}
