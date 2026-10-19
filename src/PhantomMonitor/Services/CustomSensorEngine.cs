using System.Globalization;
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

    private const int MaxFormulaNodes = 256;

    private readonly List<(Node Expression, string Label)> _definitions;

    private CustomSensorEngine(List<(Node Expression, string Label)> definitions) => _definitions = definitions;

    /// <summary>
    /// Cria a engine a partir do config (null/instância vazia = null: nenhum custo
    /// por ciclo). Fórmulas inválidas são descartadas individualmente, com nota no
    /// log de erros — as demais continuam válidas, mantendo a ordem do config.
    /// </summary>
    public static CustomSensorEngine? FromConfig(AppConfig? config)
    {
        var entries = config?.CustomSensors;
        if (entries is not { Count: > 0 }) return null;

        var definitions = new List<(Node, string)>(entries.Count);
        for (var index = 0; index < entries.Count && definitions.Count < MaxSensors; index++)
        {
            var entry = entries[index];
            if (entry is null || string.IsNullOrWhiteSpace(entry.Formula)) continue;

            var expression = FormulaParser.Parse(entry.Formula);
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
        return FormulaParser.Parse(formula) is not null;
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
            if (!expression.TryEval(data, out var value)) continue;
            if (!double.IsFinite(value)) continue; // div/0, NaN, ±∞ → sem amostra neste ciclo

            data.SetValue(new SensorKey(SensorType.Custom, i + 1), value, label);
        }
    }

    private static class FormulaParser
    {
        public static Node? Parse(string formula)
        {
            try
            {
                var parser = new Parser(formula);
                var node = parser.ParseExpression();
                parser.SkipSpaces();
                return parser.Eof && node != null ? node : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    // ---------- AST ----------

    private abstract class Node
    {
        public abstract bool TryEval(SensorData data, out double value);
    }

    private sealed class NumberNode(double value) : Node
    {
        public override bool TryEval(SensorData data, out double value)
        {
            value = _value;
            return true;
        }

        private readonly double _value = value;
    }

    /// <summary>Referência a série ("GpuTemperature", "Custom#1", "Gpu*", "MeuSensor").</summary>
    private sealed class SeriesNode(string token) : Node
    {
        public override bool TryEval(SensorData data, out double value) =>
            TryResolveSeries(data, _token, out value);

        private readonly string _token = token;
    }

    private sealed class UnaryNode(Node inner) : Node
    {
        public override bool TryEval(SensorData data, out double value)
        {
            if (!_inner.TryEval(data, out var v))
            {
                value = 0;
                return false;
            }

            value = -v;
            return true;
        }

        private readonly Node _inner = inner;
    }

    private sealed class BinaryNode(char op, Node left, Node right) : Node
    {
        public override bool TryEval(SensorData data, out double value)
        {
            if (!_left.TryEval(data, out var l) || !_right.TryEval(data, out var r))
            {
                value = 0; // operando não resolvido → série escondida neste ciclo
                return false;
            }

            value = _op switch
            {
                '+' => l + r,
                '-' => l - r,
                '*' => l * r,
                _ => l / r // div/0 → ±∞/NaN; o chamador descarta
            };
            return double.IsFinite(value);
        }

        private readonly char _op = op;
        private readonly Node _left = left;
        private readonly Node _right = right;
    }

    private sealed class FuncNode(string name, List<Node> args) : Node
    {
        public override bool TryEval(SensorData data, out double value)
        {
            var resolved = new List<double>(_args.Count);
            foreach (var arg in _args)
            {
                if (!arg.TryEval(data, out var v))
                {
                    value = 0;
                    return false; // argumento não resolvido → série escondida
                }
                resolved.Add(v);
            }

            value = Name switch
            {
                "abs" => resolved.Count == 1 ? Math.Abs(resolved[0]) : double.NaN,
                "min" => resolved.Count > 0 ? resolved.Min() : double.NaN,
                _ => resolved.Count > 0 ? resolved.Max() : double.NaN // "max"
            };
            return double.IsFinite(value);
        }

        private string Name { get; } = name.ToLowerInvariant();
        private readonly List<Node> _args = args;
    }

    // ---------- Parser ----------

    private sealed class Parser(string source)
    {
        private int _pos;
        private int _budget = MaxFormulaNodes;
        private readonly string _s = source;

        public bool Eof
        {
            get
            {
                SkipSpaces();
                return _pos >= _s.Length;
            }
        }

        public Node? ParseExpression()
        {
            var left = ParseTerm();
            if (left is null) return null;

            while (true)
            {
                SkipSpaces();
                var op = NextChar();
                if (op is not ('+' or '-')) break;
                Advance();
                var right = ParseTerm();
                if (right is null) return null;
                if (!ReserveNode()) return null;
                left = new BinaryNode(op, left, right);
            }
            return left;
        }

        private Node? ParseTerm()
        {
            var left = ParseFactor();
            if (left is null) return null;

            while (true)
            {
                SkipSpaces();
                var op = NextChar();
                if (op is not ('*' or '/')) break;
                Advance();
                var right = ParseFactor();
                if (right is null) return null;
                if (!ReserveNode()) return null;
                left = new BinaryNode(op, left, right);
            }
            return left;
        }

        private Node? ParseFactor()
        {
            if (!ReserveNode()) return null;
            SkipSpaces();
            if (_pos >= _s.Length) return null;

            var c = _s[_pos];

            // Unário -/+
            if (c is '-' or '+')
            {
                Advance();
                var inner = ParseFactor();
                if (inner is null) return null;
                return c == '-' ? new UnaryNode(inner) : inner;
            }

            // Parênteses
            if (c == '(')
            {
                Advance();
                var expr = ParseExpression();
                if (expr is null) return null;
                SkipSpaces();
                if (NextChar() != ')') return null;
                Advance();
                return expr;
            }

            // Referência {Chave} (1..64 chars)
            if (c == '{')
            {
                var close = _s.IndexOf('}', _pos + 1);
                if (close < 0) return null;
                var token = _s[(_pos + 1)..close].Trim();
                if (token.Length is 0 or > 64) return null;
                AdvanceTo(close + 1);
                return new SeriesNode(token);
            }

            // Função min()/max()/abs(...) — restantes são erro.
            if (char.IsAsciiLetter(c) || c == '_')
            {
                var start = _pos;
                while (_pos < _s.Length && (char.IsAsciiLetterOrDigit(_s[_pos]) || _s[_pos] == '_')) _pos++;
                var name = _s[start.._pos];

                SkipSpaces();
                if (NextChar() != '(') return null;
                Advance();
                SkipSpaces();

                var args = new List<Node>();
                if (NextChar() != ')')
                {
                    while (true)
                    {
                        var arg = ParseExpression();
                        if (arg is null) return null;
                        args.Add(arg);
                        SkipSpaces();
                        var terminator = NextChar();
                        if (terminator == ',') { Advance(); continue; }
                        if (terminator == ')') break;
                        return null;
                    }
                }

                SkipSpaces();
                if (NextChar() != ')') return null;
                Advance();
                return name.Equals("min", StringComparison.OrdinalIgnoreCase) ? new FuncNode("min", args)
                     : name.Equals("max", StringComparison.OrdinalIgnoreCase) ? new FuncNode("max", args)
                     : name.Equals("abs", StringComparison.OrdinalIgnoreCase) ? new FuncNode("abs", args)
                     : null;
            }

            // Número (decimal com ponto; vírgula é separador de argumento).
            if (char.IsAsciiDigit(c) || c == '.')
            {
                var start = _pos;
                while (_pos < _s.Length && (char.IsAsciiDigit(_s[_pos]) || _s[_pos] == '.')) _pos++;
                return double.TryParse(_s[start.._pos], NumberStyles.Integer | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value)
                    ? new NumberNode(value)
                    : null;
            }

            return null;
        }

        public void SkipSpaces()
        {
            while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
        }

        public char NextChar()
        {
            SkipSpaces();
            return _pos < _s.Length ? _s[_pos] : '\0';
        }

        private void Advance() => _pos++;

        private void AdvanceTo(int position) => _pos = Math.Max(position, _pos);

        private bool ReserveNode()
        {
            if (_budget <= 0) return false;
            _budget--;
            return true;
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
