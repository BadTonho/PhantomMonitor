using System.Globalization;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

internal abstract class CustomSensorExpression
{
    public abstract bool TryEvaluate(SensorData data, out double value);
}

internal static class CustomSensorFormulaParser
{
    private const int MaxFormulaNodes = 256;

    public static CustomSensorExpression? Parse(string formula)
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

    // ---------- AST ----------

    private sealed class NumberNode(double value) : CustomSensorExpression
    {
        public override bool TryEvaluate(SensorData data, out double value)
        {
            value = _value;
            return true;
        }

        private readonly double _value = value;
    }

    /// <summary>Referência a série ("GpuTemperature", "Custom#1", "Gpu*", "MeuSensor").</summary>
    private sealed class SeriesNode(string token) : CustomSensorExpression
    {
        public override bool TryEvaluate(SensorData data, out double value) =>
            CustomSensorEngine.TryResolveSeries(data, _token, out value);

        private readonly string _token = token;
    }

    private sealed class UnaryNode(CustomSensorExpression inner) : CustomSensorExpression
    {
        public override bool TryEvaluate(SensorData data, out double value)
        {
            if (!_inner.TryEvaluate(data, out var v))
            {
                value = 0;
                return false;
            }

            value = -v;
            return true;
        }

        private readonly CustomSensorExpression _inner = inner;
    }

    private sealed class BinaryNode(char op, CustomSensorExpression left, CustomSensorExpression right) : CustomSensorExpression
    {
        public override bool TryEvaluate(SensorData data, out double value)
        {
            if (!_left.TryEvaluate(data, out var l) || !_right.TryEvaluate(data, out var r))
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
        private readonly CustomSensorExpression _left = left;
        private readonly CustomSensorExpression _right = right;
    }

    private sealed class FuncNode(string name, List<CustomSensorExpression> args) : CustomSensorExpression
    {
        public override bool TryEvaluate(SensorData data, out double value)
        {
            var resolved = new List<double>(_args.Count);
            foreach (var arg in _args)
            {
                if (!arg.TryEvaluate(data, out var v))
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
        private readonly List<CustomSensorExpression> _args = args;
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

        public CustomSensorExpression? ParseExpression()
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

        private CustomSensorExpression? ParseTerm()
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

        private CustomSensorExpression? ParseFactor()
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

                var args = new List<CustomSensorExpression>();
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
}

