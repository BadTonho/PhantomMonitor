using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhantomMonitor.Models;

/// <summary>
/// Chave de uma série de telemetria: categoria (<see cref="SensorType"/>) + índice de instância.
/// Índice 0 é a série canônica/agregada usada por todas as APIs existentes (linhas fixas do OSD,
/// painel principal, alertas). Índices ≥ 1 são séries por instância que identificam hardware
/// específico: <c>GpuCore#1</c> (segunda GPU), <c>CpuTemperature#3</c> (núcleo 3), <c>FanSpeed#2</c>
/// (segunda ventoinha), <c>DiskRead#1</c> (disco de índice 1) e assim por diante.
/// Formato textual: <c>GpuCore</c> ou <c>GpuCore#1</c>.
/// </summary>
[JsonConverter(typeof(SensorKeyJsonConverter))]
public readonly struct SensorKey : IEquatable<SensorKey>, IComparable<SensorKey>
{
    public SensorType Type { get; }

    /// <summary>Índice da instância (0 = série canônica/agregada; ≥ 1 = instância específica).</summary>
    public int Index { get; }

    public SensorKey(SensorType type, int index)
    {
        Type = type;
        Index = index < 0 ? 0 : index;
    }

    /// <summary>Série canônica (índice 0), a única visível nas APIs legadas.</summary>
    public static SensorKey Primary(SensorType type) => new(type, 0);

    public bool IsPrimary => Index == 0;

    public override string ToString() => Index == 0 ? Type.ToString() : $"{Type}#{Index}";

    public bool Equals(SensorKey other) => Type == other.Type && Index == other.Index;
    public override bool Equals(object? obj) => obj is SensorKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Type, Index);

    public int CompareTo(SensorKey other)
    {
        var byType = string.CompareOrdinal(Type.ToString(), other.Type.ToString());
        return byType != 0 ? byType : Index.CompareTo(other.Index);
    }

    public static bool operator ==(SensorKey left, SensorKey right) => left.Equals(right);
    public static bool operator !=(SensorKey left, SensorKey right) => !left.Equals(right);

    /// <summary>
    /// Analisa a forma textual da chave ("GpuCore" ou "GpuCore#1"). Sensível a maiúsculas
    /// para bater com os nomes canônicos do enum.
    /// </summary>
    public static bool TryParse(string? text, out SensorKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var hash = text.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0)
        {
            if (!Enum.TryParse(text, ignoreCase: false, out SensorType type)) return false;
            key = new SensorKey(type, 0);
            return true;
        }

        if (!Enum.TryParse(text[..hash], ignoreCase: false, out SensorType baseType)) return false;
        if (!int.TryParse(text[(hash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index <= 0)
            return false;

        key = new SensorKey(baseType, index);
        return true;
    }

    public static SensorKey Parse(string text) =>
        TryParse(text, out var key) ? key : throw new FormatException($"Chave de sensor inválida: '{text}'");

    /// <summary>
    /// Confere a chave contra um padrão da whitelist do OSD. Padrões suportados:
    /// "*" (tudo), "Gpu*" (qualquer categoria com esse prefixo), "*Total" (sufixo) e
    /// "CpuTemperature#*" (qualquer instância da categoria). Sem distinção de maiúsculas.
    /// </summary>
    public bool MatchesPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        var text = ToString();
        if (pattern == "*") return true;

        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return prefix.Length == 0 || text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.StartsWith('*'))
        {
            var suffix = pattern[1..];
            return suffix.Length > 0 && text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return text.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Serializa a chave como texto ("GpuCore" / "GpuCore#1") tanto como valor quanto como
/// propriedade de dicionário — o snapshot JSON usa cada série como chave.
/// </summary>
public sealed class SensorKeyJsonConverter : JsonConverter<SensorKey>
{
    public override SensorKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        SensorKey.Parse(reader.GetString() ?? throw new JsonException("Chave de sensor nula."));

    public override void Write(Utf8JsonWriter writer, SensorKey value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());

    public override void WriteAsPropertyName(Utf8JsonWriter writer, SensorKey value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.ToString());

    public override SensorKey ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        SensorKey.Parse(reader.GetString() ?? throw new JsonException("Chave de sensor nula."));
}

/// <summary>Unidade de exibição padrão de cada categoria de série (SDK de telemetria e inventário).</summary>
public static class SensorUnits
{
    public static string Of(SensorType type) => type switch
    {
        SensorType.CpuTemperature or SensorType.GpuTemperature or SensorType.GpuHotSpot or
        SensorType.MotherboardTemp or SensorType.VrmTemperature or SensorType.RamTemperature or
        SensorType.DiskTemperature => "°C",

        SensorType.CpuTotal or SensorType.CpuPerCore or SensorType.GpuCore or SensorType.GpuMemory or
        SensorType.RamUsage or SensorType.DiskUsage or SensorType.DiskHealth or SensorType.GpuFan or
        SensorType.BatteryLevel or SensorType.BatteryWearLevel => "%",

        SensorType.FanSpeed => "RPM",

        SensorType.CpuClock or SensorType.GpuCoreClock or SensorType.GpuMemoryClock => "MHz",

        SensorType.CpuVoltage or SensorType.GpuVoltage or SensorType.RailVoltage or
        SensorType.BatteryVoltage => "V",

        SensorType.CpuPower or SensorType.GpuPower or SensorType.BatteryPower => "W",

        SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal => "MB",

        SensorType.RamAvailable or SensorType.RamUsed or SensorType.RamTotal or
        SensorType.DiskTotalRead or SensorType.DiskTotalWritten => "GB",

        SensorType.DiskRead or SensorType.DiskWrite or SensorType.NetworkUp or SensorType.NetworkDown => "B/s",

        SensorType.NetworkUpTotal or SensorType.NetworkDownTotal => "B",

        SensorType.Fps or SensorType.FpsAvg or SensorType.FpsMin or SensorType.Fps1Low or
        SensorType.Fps01Low or SensorType.FpsMax => "FPS",

        SensorType.FrametimeMs => "ms",

        _ => ""
    };
}
