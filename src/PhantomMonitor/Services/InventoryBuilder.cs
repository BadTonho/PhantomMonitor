using System.Globalization;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Monta o inventário de hardware a partir do snapshot atual:
/// 1) as <b>séries publicadas</b> (canônicas + por instância, já com rótulo/unidade/valor),
///    que são o que qualquer consumidor (OSD, alertas, CSV, SDK) enxerga;
/// 2) a <b>árvore de hardware</b> detectada, decodificada das linhas de diagnóstico
///    <c>[HWR]</c> que o serviço já produz por ciclo (mesma fonte do DebugWindow).
/// </summary>
public static class InventoryBuilder
{
    public static List<InventorySeries> BuildSeries(SensorData data)
    {
        var list = new List<InventorySeries>(data.Values.Count + data.Series.Count);
        foreach (var key in data.GetSeriesKeys())
        {
            var value = data.GetValue(key);
            list.Add(new InventorySeries
            {
                Key = key.ToString(),
                Label = data.GetLabel(key),
                Unit = SensorUnits.Of(key.Type),
                Value = value,
                ValueText = string.IsNullOrEmpty(SensorUnits.Of(key.Type))
                    ? value.ToString("F2", CultureInfo.InvariantCulture)
                    : SensorFormatter.FormatSeries(key, value),
                Canonical = key.IsPrimary
            });
        }
        return list;
    }

    public static List<InventoryHardware> BuildHardware(SensorData data)
    {
        var hardware = new List<InventoryHardware>();

        foreach (var line in data.SensorDebugInfo)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("[HWR]", StringComparison.Ordinal))
                continue;

            var inner = trimmed["[HWR]".Length..].Trim();
            var separator = inner.IndexOf(':', StringComparison.Ordinal);
            var node = separator > 0
                ? new InventoryHardware { Type = inner[..separator].Trim(), Name = inner[(separator + 1)..].Trim(), Depth = LineDepth(line) }
                : new InventoryHardware { Type = inner, Name = inner, Depth = LineDepth(line) };

            hardware.Add(node);
        }

        return hardware;
    }

    /// <summary>Profundidade da árvore decodificada pela indentação (2 espaços por nível).</summary>
    private static int LineDepth(string line) => (line.Length - line.TrimStart().Length) / 2;
}

public sealed class InventorySeries
{
    /// <summary>Chave completa da série ("GpuCore" ou "GpuCore#1").</summary>
    public string Key { get; set; } = "";

    /// <summary>Rótulo do sensor de origem ("Nuvoton NCT6791D — Fan #2 (Fan)").</summary>
    public string Label { get; set; } = "";

    public string Unit { get; set; } = "";
    public double Value { get; set; }

    /// <summary>Valor já formatado com a unidade (ex.: "45°C", "3.2 GB/s").</summary>
    public string ValueText { get; set; } = "";

    /// <summary>True para as séries canônicas legadas (índice 0).</summary>
    public bool Canonical { get; set; }
}

public sealed class InventoryHardware
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public int Depth { get; set; }

    public override string ToString() =>
        string.IsNullOrEmpty(Name) ? Type : $"{Type}: {Name}";
}