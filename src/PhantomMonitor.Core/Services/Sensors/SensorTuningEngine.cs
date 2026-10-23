using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Cadência de amostragem por série + suavização (P9.1). A leitura física continua
/// global (o tick do serviço já atualiza tudo de uma vez); este estágio decide,
/// por série, há quanto tempo o valor publicado pode ficar sem se refrescar e qual
/// janela de média móvel atravessa as amostras frescas. Entre amostras a série
/// REPETE o último valor publicado (a leitura vale "a partir da última amostragem",
/// como no HWiNFO), e o conjunto de séries repetidas volta para o <c>Apply</c>
/// para que o <see cref="SeriesStatsTracker"/> NÃO accounte amostra duplicada —
/// min/máx/média da sessão amostram na cadência configurada, e o CSV/JSON/SDK
/// continuam publicando o valor por tick (timeline constante até o refresh).
///
/// Padrões de casamento reusam <see cref="SensorKey.MatchesPattern"/>: chave
/// canônica exata ("GpuTemperature"), por instância ("GpuTemperature#1"), por
/// categoria de instância ("CpuTemperature#*") ou prefixo de família ("Gpu*",
/// pega canônica e instâncias). Quando duas regras casam a mesma série, a ÚLTIMA
/// da lista vence. Série ausente no snapshot é ignorada (nada a repetir); quando
/// ela volta, a primeira amostra é sempre fresca. Suavização aplica-se apenas a
/// séries de "leitura instantânea" (temperatura, uso de dispersão): suavizar
/// contadores/somatórios não faz sentido físico e fica por conta do usuário
/// evitar (o engine não distingue).
/// </summary>
public sealed class SensorTuningEngine
{
    /// <summary>Teto de regras (mesma política das customSensors).</summary>
    public const int MaxTunings = 64;

    /// <summary>Teto da janela de suavização (amostras frescas por média).</summary>
    public const int MaxSmoothing = 60;

    private readonly List<Rule> _rules = [];
    private readonly Func<DateTime> _clock;

    private sealed class Rule(string series, int intervalMs, int smoothing)
    {
        public string Series { get; } = series;
        public int IntervalMs { get; } = intervalMs; // 0 = toda amostra é publicada
        public int Smoothing { get; } = smoothing;   // janela ≥ 2 quando ativo

        // Estado por série casada: relojoagem do próximo refresh, último valor
        // publicado (para repetir entre amostras) e janela da média móvel.
        public readonly Dictionary<SensorKey, DateTime> NextDue = [];
        public readonly Dictionary<SensorKey, double> LastPublished = [];
        public readonly Dictionary<SensorKey, FrametimeBuffer> Window = [];
    }

    public SensorTuningEngine(IReadOnlyList<SensorTuningConfig> tunings, Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        foreach (var tuning in tunings)
        {
            if (string.IsNullOrWhiteSpace(tuning.Series)) continue;
            _rules.Add(new Rule(
                tuning.Series.Trim(),
                tuning.IntervalMs,
                tuning.Smoothing)); // 0 = sem suavização (o gate de Apply é Smoothing >= 2)
        }
    }

    /// <summary>
    /// Cria o engine a partir do config (mesma política do CustomSensorEngine):
    /// config vazio/ausente devolve <c>null</c> e nenhum dos serviços aloca estado.
    /// </summary>
    public static SensorTuningEngine? FromConfig(AppConfig? config)
    {
        if (config?.SensorTunings is not { Count: > 0 }) return null;
        return new SensorTuningEngine(config.SensorTunings);
    }

    /// <summary>
    /// Aplica cadência e suavização sobre o snapshot do ciclo (mutação in place)
    /// e devolve as séries que REPETIRAM o valor anterior no ciclo atual (o
    /// chamador passa o conjunto para o SeriesStatsTracker pular essas amostras).
    /// </summary>
    public HashSet<SensorKey> Apply(SensorData data)
    {
        var repeated = new HashSet<SensorKey>();
        if (_rules.Count == 0) return repeated;

        var now = _clock();
        var keys = data.GetSeriesKeys();

        // Valores ABSOLUTOS lidos ANTES de qualquer regra: uma regra anterior que
        // repete sobrescreve o snapshot; as seguintes têm que publicar a leitura
        // original da mapper, senão "fresh" escreveria valor vencido.
        var raws = new Dictionary<SensorKey, double>(keys.Count);
        foreach (var key in keys)
            raws[key] = data.GetValue(key);

        foreach (var rule in _rules)
        {
            foreach (var key in keys)
            {
                if (!key.MatchesPattern(rule.Series)) continue;

                // Cadência: dentro da janela, a série repete o último valor publicado.
                if (rule.IntervalMs > 0)
                {
                    if (rule.NextDue.TryGetValue(key, out var due) && now < due)
                    {
                        if (rule.LastPublished.TryGetValue(key, out var last))
                            WriteValue(data, key, last);
                        repeated.Add(key);
                        continue;
                    }

                    rule.NextDue[key] = now + TimeSpan.FromMilliseconds(rule.IntervalMs);
                }

                // Reinício da marca de repetição: quando várias regras casam a mesma
                // série, a ÚLTIMA decide (cadência e estatística) — uma regra mais
                // nova sempre-fresco cancela a repetição de uma anterior.
                repeated.Remove(key);

                // Amostra fresca: suaviza pela média da janela ( buffers só recebem
                // valores finitos, como no CustomSensorEngine).
                var raw = raws[key];
                var published = raw;
                if (rule.Smoothing >= 2 && double.IsFinite(raw))
                {
                    if (!rule.Window.TryGetValue(key, out var window))
                        rule.Window[key] = window = new FrametimeBuffer(rule.Smoothing);
                    window.Add((float)raw);
                    published = window.GetAverage(); // sem cópia + LINQ por amostra
                }

                WriteValue(data, key, published);
                rule.LastPublished[key] = published;
            }
        }

        return repeated;
    }

    private static void WriteValue(SensorData data, SensorKey key, double value)
    {
        // Caminho espelhado do SensorData.SetValue: canônica no dicionário por
        // SensorType, instância em Series — sem tocar rótulos (não mudam com a
        // cadência).
        if (key.IsPrimary) data.Values[key.Type] = value;
        else data.Series[key] = value;
    }
}
