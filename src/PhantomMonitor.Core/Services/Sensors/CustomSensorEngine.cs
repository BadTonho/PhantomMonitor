using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Engine de sensores customizados (paridade HWiNFO): cada entrada do config
/// <c>customSensors</c> é uma fórmula derivada sobre outras séries do snapshot
/// — ex. <c>"{"CpuTemperature"} - {"CpuTemperature#1"}"</c> no JSON de config.
/// Sintaxe aceita: operadores <c>+ - * /</c> (precedência padrão), parênteses,
/// unário <c>-</c>, números decimais com ponto (vírgula é separador de argumento
/// nas funções), funções <c>min(...)</c>/<c>max(...)</c> variádicas e
/// <c>abs(...)</c>, e referências <c>{"Chave"}</c> que seguem a mesma resolução
/// das templates do OSD: chave explícita (<c>GpuTemperature</c> ou
/// <c>GpuTemperature#1</c>, com fallback para a primeira instância quando a
/// canônica não existe), padrão wildcard (<c>Gpu*</c>, <c>Custom#*</c>) e, por
/// último, rótulo amigável exato — o que permite fórmulas referenciando outros
/// sensores customizados pelo nome.
/// A fórmula é compilada uma vez (AST); a avaliação por ciclo só resolve as
/// referências contra o snapshot atual. Ciclo em que qualquer referência ou o
/// resultado não resolve/é não-finito (divisão por zero): o sensor não publica
/// valor — mesma política "linhas escondidas até resolverem" das templates.
/// A série sai como <c>Custom#i</c> (posição da fórmula no config, a partir de 1;
/// rótulo = nome amigável, com unidade opcional exibida entre parênteses) e é
/// avaliada ANTES do commit das estatísticas por série — logo, min/máx/média
/// da sessão, CSV/JSON, SDK, HTTP, inventário e alertas a enxergam como uma
/// série nativa. Alertas miram <c>sensor: "Custom"</c> + <c>sensorIndex</c>.
/// </summary>
public sealed class CustomSensorEngine
{
    /// <summary>Teto de fórmulas ativas (a tabela da SDK de telemetria tem 256 entradas).</summary>
    public const int MaxSensors = 64;

    private readonly List<(CustomSensorExpression Expression, string Label)> _definitions;

    private CustomSensorEngine(List<(CustomSensorExpression Expression, string Label)> definitions) => _definitions = definitions;

    /// <summary>
    /// Cria a engine a partir do config (null/instância vazia = null: nenhum custo
    /// por ciclo). Fórmulas inválidas são descartadas individualmente, com nota no
    /// log de erros — as demais continuam válidas, mantendo a ordem do config.
    /// </summary>
    public static CustomSensorEngine? FromConfig(AppConfig? config)
    {
        var entries = config?.CustomSensors;
        if (entries is not { Count: > 0 }) return null;

        var definitions = new List<(CustomSensorExpression, string)>(entries.Count);
        for (var index = 0; index < entries.Count && definitions.Count < MaxSensors; index++)
        {
            var entry = entries[index];
            if (entry is null || string.IsNullOrWhiteSpace(entry.Formula)) continue;

            var expression = CustomSensorFormulaParser.Parse(entry.Formula);
            if (expression is null)
            {
                ErrorLog.Write("CustomSensorEngine",
                    $"Fórmula descartada (sintaxe inválida): '{entry.Formula}' [{entry.Name}]");
                continue;
            }

            var label = string.IsNullOrWhiteSpace(entry.Unit)
                ? entry.Name
                : $"{entry.Name} ({entry.Unit})";
            definitions.Add((expression, label));
        }

        return definitions.Count > 0 ? new CustomSensorEngine(definitions) : null;
    }

    /// <summary>Validação sintática da fórmula (usada pelo config.Normalize e testável isoladamente).</summary>
    public static bool IsFormulaValid(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return false;
        return CustomSensorFormulaParser.Parse(formula) is not null;
    }

    /// <summary>
    /// Avalia todas as fórmulas contra o snapshot (as definições são processadas na
    /// ordem do config, e fórmulas podem referenciar sensores customizados anteriores
    /// pelo nome). Séries são publicadas com índice estável da posição no config.
    /// </summary>
    public void Evaluate(SensorData data)
    {
        for (var i = 0; i < _definitions.Count; i++)
        {
            var (expression, label) = _definitions[i];
            if (!expression.TryEvaluate(data, out var value)) continue;
            if (!double.IsFinite(value)) continue; // div/0, NaN, ±∞ → sem amostra neste ciclo

            data.SetValue(new SensorKey(SensorType.Custom, i + 1), value, label);
        }
    }

    // ---------- Resolução de referências ----------

    /// <summary>
    /// Resolve uma referência {"..."} contra o snapshot (mesma semântica do
    /// <c>TryResolveTemplateValue</c> do OSD): 1) chave explícita com fallback para
    /// a primeira instância da categoria; 2) padrão wildcard; 3) rótulo amigável
    /// exato (permite {"MeuSensor"} referenciando outro sensor customizado).
    /// </summary>
    public static bool TryResolveSeries(SensorData data, string token, out double value)
    {
        value = 0;
        if (data is null || string.IsNullOrWhiteSpace(token)) return false;

        // 1) Chave explícita.
        if (SensorKey.TryParse(token, out var key))
        {
            if (key.IsPrimary)
            {
                if (data.Values.TryGetValue(key.Type, out var canonical))
                {
                    value = canonical;
                    return true;
                }

                // Sem canônica: primeira instância publicada da categoria
                // (FirstOrDefault em struct: sem match vira Index 0 = teste abaixo).
                var instance = data.Series.Keys
                    .Where(s => s.Type == key.Type && s.Index > 0)
                    .OrderBy(s => s.Index)
                    .FirstOrDefault();
                if (instance.Index > 0 && data.Series.TryGetValue(instance, out var variant))
                {
                    value = variant;
                    return true;
                }
                return false;
            }

            return data.Series.TryGetValue(key, out value);
        }

        // 2) Padrão wildcard: primeiro match (canônicas primeiro, depois instâncias).
        if (token.Contains('*'))
        {
            foreach (var candidate in data.GetSeriesKeys())
            {
                if (!candidate.MatchesPattern(token)) continue;
                value = data.GetValue(candidate);
                return true;
            }
            return false;
        }

        // 3) Rótulo amigável: nome do sensor customizado (com unidade opcional no
        // rótulo — "DeltaTemp (°C)" casa o {"DeltaTemp"}). Exato primeiro, depois o
        // nome-base antes de " (": permite fórmulas referenciando outros customs.
        foreach (var candidate in data.GetSeriesKeys())
        {
            var label = data.GetLabel(candidate);
            if (string.Equals(label, token, StringComparison.OrdinalIgnoreCase))
            {
                value = data.GetValue(candidate);
                return true;
            }
        }
        foreach (var candidate in data.GetSeriesKeys())
        {
            var label = data.GetLabel(candidate);
            var baseName = label;
            var paren = label.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) baseName = label[..paren];
            if (baseName.Length == 0 || baseName.Length == label.Length) continue; // sem unidade: já coberto acima
            if (!string.Equals(baseName, token, StringComparison.OrdinalIgnoreCase)) continue;
            value = data.GetValue(candidate);
            return true;
        }

        return false;
    }
}
