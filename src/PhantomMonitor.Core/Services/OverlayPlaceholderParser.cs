using System.Globalization;
using System.Text;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Parser/resolvedor de placeholders do OSD — porta fiel da lógica do WPF
/// (OverlayWindow P6.6/P8.5): compiladores de templates "{Chave}", "{Chave:N}"
/// (N = 0–3 decimais, padrão 1), padrões wildcard ("{Gpu*}", primeiro match) e
/// nomes amigáveis ("{DeltaTemp}" — o rótulo da série); e a resolução numérica
/// das séries: chave canônica → primeira instância publicada → wildcard/rótulo.
/// Servidores do mesmo resolver: templates P6.6 do painel e os gráficos por
/// série P8.5. Deliberadamente tolerante: "{" solitário permanece literal e
/// chave inválida só sobrevive como padrão (linha escondida até casa).
/// </summary>
public static class OverlayPlaceholderParser
{
    /// <summary>Segmento compilado de uma template: literal puro OU placeholder.</summary>
    public sealed record Segment(string Literal, string Pattern, SensorKey Key, int Decimals)
    {
        public bool IsLiteral => Pattern.Length == 0;
    }

    /// <summary>Compila um template; PlaceIter não vazia nunca (literal fallback).</summary>
    public static List<Segment> Compile(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var segments = new List<Segment>();
        var sb = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
            {
                sb.Append(text[i]);
                continue;
            }

            var close = text.IndexOf('}', i + 1);
            if (close < 0) { sb.Append(text[i..]); break; }

            var token = text[(i + 1)..close].Trim();
            i = close;

            var decimals = 1;
            var colon = token.IndexOf(':');
            if (colon > 0 &&
                int.TryParse(token[(colon + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                decimals = Math.Clamp(n, 0, 3);
                token = token[..colon].Trim();
            }

            if (token.Length == 0)
            {
                segments.Add(new Segment(sb.ToString(), "", default, 0));
                sb.Clear();
                continue;
            }

            if (SensorKey.TryParse(token, out var key))
                segments.Add(new Segment(sb.ToString(), "", key, decimals));
            else
                segments.Add(new Segment(sb.ToString(), token, default, decimals));
            sb.Clear();
        }

        if (sb.Length > 0) segments.Add(new Segment(sb.ToString(), "", default, 0));
        return segments;
    }

    /// <summary>
    /// Resolve um placeholder para valor numérico; false quando NADA casa (linha oculta).
    /// Chave canônica: valor canônico primeiro, senão a primeira instância da categoria;
    /// chave com índice: exata; texto não-parseado: primeiro match de padrão (e depois
    /// rótulo amigável: exato ou nome-base antes de " (").
    /// </summary>
    public static bool TryResolveValue(SensorData data, Segment segment, out double value)
    {
        value = 0;

        if (segment.IsLiteral) return false;

        if (segment.Pattern.Length > 0)
        {
            foreach (var key in data.GetSeriesKeys())
            {
                if (!key.MatchesPattern(segment.Pattern)) continue;
                value = ReadSeriesValue(data, key);
                return true;
            }

            foreach (var key in data.GetSeriesKeys())
            {
                if (!LabelMatchesPattern(data.GetLabel(key), segment.Pattern)) continue;
                value = ReadSeriesValue(data, key);
                return true;
            }

            return false;
        }

        var k = segment.Key;
        if (k.IsPrimary)
        {
            if (data.Values.TryGetValue(k.Type, out var canonical)) { value = canonical; return true; }

            var instance = data.Series.Keys
                .Where(s => s.Type == k.Type && s.Index > 0)
                .OrderBy(s => s.Index)
                .FirstOrDefault();
            if (instance is { } found && data.Series.TryGetValue(found, out var instanceValue)) { value = instanceValue; return true; }
            return false;
        }

        return data.Series.TryGetValue(k, out value);
    }

    /// <summary>Valor com verificação de presença (GetValue devolve 0 para série ausente).</summary>
    public static double ReadSeriesValue(SensorData data, SensorKey key) =>
        key.IsPrimary
            ? data.Values.TryGetValue(key.Type, out var value) ? value : 0
            : data.Series.TryGetValue(key, out var seriesValue) ? seriesValue : 0;

    /// <summary>
    /// Padrão casa o rótulo por igualdade ou por nome-base (rótulo "DeltaTemp (°C)"
    /// casa "DeltaTemp"): mesma regra da resolução de fórmulas dos sensores customizados.
    /// </summary>
    public static bool LabelMatchesPattern(string label, string pattern)
    {
        if (string.Equals(label, pattern, StringComparison.OrdinalIgnoreCase)) return true;
        var paren = label.IndexOf(" (", StringComparison.Ordinal);
        if (paren <= 0 || paren == label.Length - 2) return false;
        return string.Equals(label[..paren], pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Renderiza o texto do template; false = algum placeholder não resolveu.</summary>
    public static bool TryRender(SensorData data, IReadOnlyList<Segment> segments, out string text)
    {
        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            if (segment.IsLiteral)
            {
                sb.Append(segment.Literal);
                continue;
            }

            if (!TryResolveValue(data, segment, out var value)) { text = ""; return false; }
            sb.Append(value.ToString($"F{segment.Decimals}", CultureInfo.InvariantCulture));
        }

        text = sb.ToString();
        return text.Length > 0;
    }
}
