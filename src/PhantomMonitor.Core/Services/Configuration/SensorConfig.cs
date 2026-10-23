namespace PhantomMonitor.Services;

/// <summary>
/// Cadência de amostragem + suavização por série (P9.1; ver SensorTuningEngine):
/// entre amostras a série repete o último valor publicado e as estatísticas da
/// sessão contam só amostras frescas — min/máx/média amostram na cadência
/// configurada. A casamento do padrão reusa SensorKey.MatchesPattern ("GpuTemperature"
/// canônica, "GpuTemperature#1", "CpuTemperature#*", "Gpu*"...); duas regras que
/// casam a mesma série: a última vence. Série de contador/somatório não deve ser
/// suavizada (não faz sentido físico).
/// </summary>
public sealed class SensorTuningConfig
{
    /// <summary>Padrão da série alvo no formato SensorKey ("GpuTemperature", "Custom#1"...).</summary>
    public string Series { get; set; } = "";

    /// <summary>Intervalo de refresh em ms (0 = publicar toda amostra do tick global; clamped ao tick).</summary>
    public int IntervalMs { get; set; } = 0;

    /// <summary>Janela da média móvel sobre amostras frescas (0/1 = desligado; teto 60).</summary>
    public int Smoothing { get; set; } = 0;
}

public sealed class SensorConfig
{
    public bool Cpu { get; set; } = true;
    public bool Gpu { get; set; } = true;
    public bool Ram { get; set; } = true;
    public bool Disk { get; set; } = true;
    public bool Network { get; set; } = false;
    public bool Motherboard { get; set; } = false;
}

/// <summary>
/// Sensor customizado (paridade HWiNFO "custom user sensor"): fórmula derivada de
/// outras séries do snapshot, avaliada a cada ciclo pela <see cref="CustomSensorEngine"/>
/// e publicada como série <c>Custom#i</c> (i = posição da fórmula no config, ≥ 1).
/// <c>formula</c> segue a sintaxe do parser (operadores + - * /, parênteses, funções
/// min/max/abs e referências {"Chave"} — ver docs/funções/sensores-customizados.md).
/// <c>unit</c> é opcional e só compõe o rótulo exibido ("Média (°C)").
/// </summary>
public sealed class CustomSensorConfig
{
    /// <summary>Nome amigável (vira o rótulo da série e a referência {"Nome"} em fórmulas).</summary>
    public string Name { get; set; } = "";

    public string Formula { get; set; } = "";

    /// <summary>Unidade opcional exibida no rótulo e nas planilhas ("°C", "%", "MB"...).</summary>
    public string Unit { get; set; } = "";
}
