using System.Text.Json.Serialization;

namespace PhantomMonitor.Models;

/// <summary>Direção da comparação de uma regra de alerta.</summary>
public enum AlertComparison
{
    Above,
    Below
}

/// <summary>
/// Regra de alerta: compara um sensor com um limiar e dispara ações
/// (popup, som, script e/ou desligamento) quando a condição é atendida.
/// Disparo por borda (a condição passa de falsa para verdadeira) e respeita
/// cooldown enquanto a condição permanece verdadeira.
/// </summary>
public sealed class AlertRule
{
    public bool Enabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SensorType Sensor { get; set; } = SensorType.CpuTemperature;

    /// <summary>
    /// Índice da série alvo: 0 (padrão) usa a série canônica/agregada; ≥ 1 mira uma
    /// série por instância específica (ex.: sensor "GpuTemperature", sensorIndex 1
    /// = temperatura da GPU dedicada; "CpuTemperature" índice 2 = núcleo 2).
    /// </summary>
    public int SensorIndex { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AlertComparison Comparison { get; set; } = AlertComparison.Above;

    public double Threshold { get; set; } = 80;

    /// <summary>Cooldown específico da regra em segundos (0 = usa o cooldown global).</summary>
    public int CooldownSeconds { get; set; }

    public bool Popup { get; set; } = true;
    public bool Sound { get; set; }
    public string Script { get; set; } = "";
    public string ScriptArguments { get; set; } = "";
    public bool Shutdown { get; set; }

    /// <summary>Mensagem customizada com placeholders {sensor}, {value} e {threshold}.</summary>
    public string Message { get; set; } = "";
}
