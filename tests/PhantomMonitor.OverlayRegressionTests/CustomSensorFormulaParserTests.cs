using PhantomMonitor.Models;
using PhantomMonitor.Services;

/// <summary>Testes de lógica pura de <see cref="CustomSensorFormulaParser"/> (sem hardware).</summary>
public sealed class CustomSensorFormulaParserTests
{
    private static SensorData Data()
    {
        var data = new SensorData();
        data.SetValue(SensorType.CpuTemperature, 60);
        data.SetValue(SensorType.GpuTemperature, 70);
        data.SetValue(SensorType.CpuTotal, 0);
        return data;
    }

    private static bool Eval(string formula, out double value, SensorData? data = null)
    {
        var expression = CustomSensorFormulaParser.Parse(formula);
        if (expression is null) { value = double.NaN; return false; }
        return expression.TryEvaluate(data ?? Data(), out value);
    }

    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("10/4", 2.5)]
    [InlineData("-5+2", -3)]
    [InlineData("--5", 5)]
    [InlineData("+5", 5)]
    [InlineData("  1 +   2  ", 3)]
    [InlineData("1.5*2", 3)]
    [InlineData(".5+.5", 1)]
    [InlineData("10-2-3", 5)]
    [InlineData("100/10/2", 5)]
    [InlineData("abs(-3)", 3)]
    [InlineData("min(3,1,2)", 1)]
    [InlineData("max(3,1,2)", 3)]
    [InlineData("MAX(1, 5)", 5)]
    [InlineData("min(max(1,2),3)", 2)]
    [InlineData("max(7)", 7)]
    public void Avalia_expressoes_validas(string formula, double expected)
    {
        Assert.True(Eval(formula, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("{CpuTemperature}", 60)]
    [InlineData("{CpuTemperature} + {GpuTemperature}", 130)]
    [InlineData("({CpuTemperature} + {GpuTemperature}) / 2", 65)]
    [InlineData("max({CpuTemperature}, {GpuTemperature})", 70)]
    [InlineData("{ CpuTemperature }", 60)]
    public void Resolve_referencias_de_serie(string formula, double expected)
    {
        Assert.True(Eval(formula, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    [InlineData("1+")]
    [InlineData("*2")]
    [InlineData("1 2")]
    [InlineData("(1+2")]
    [InlineData("1+2)")]
    [InlineData("()")]
    [InlineData(".")]
    [InlineData("1.2.3")]
    [InlineData("1e3")]
    [InlineData("{}")]
    [InlineData("{   }")]
    [InlineData("{CpuTemperature")]
    [InlineData("foo(1)")]
    [InlineData("foo")]
    [InlineData("max(1,)")]
    [InlineData("max(,1)")]
    [InlineData("max(1 2)")]
    [InlineData("1 $ 2")]
    public void Rejeita_formulas_invalidas(string formula) =>
        Assert.Null(CustomSensorFormulaParser.Parse(formula));

    [Fact]
    public void Rejeita_referencia_com_mais_de_64_caracteres()
    {
        Assert.NotNull(CustomSensorFormulaParser.Parse("{" + new string('a', 64) + "}"));
        Assert.Null(CustomSensorFormulaParser.Parse("{" + new string('a', 65) + "}"));
    }

    [Fact]
    public void Limita_o_numero_de_nos_da_formula()
    {
        var deep = new string('(', 300) + "1" + new string(')', 300);
        Assert.Null(CustomSensorFormulaParser.Parse(deep));
        var wide = string.Join("+", Enumerable.Repeat("1", 300));
        Assert.Null(CustomSensorFormulaParser.Parse(wide));
        Assert.NotNull(CustomSensorFormulaParser.Parse(string.Join("+", Enumerable.Repeat("1", 20))));
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("0/0")]
    [InlineData("-1/0")]
    [InlineData("1/{CpuTotal}")]
    [InlineData("1/(2-2)")]
    public void Divisao_por_zero_nao_produz_valor(string formula) =>
        Assert.False(Eval(formula, out _));

    [Theory]
    [InlineData("max()")]
    [InlineData("min()")]
    [InlineData("abs()")]
    [InlineData("abs(1,2)")]
    public void Funcoes_com_aridade_invalida_nao_produzem_valor(string formula) =>
        Assert.False(Eval(formula, out _));

    [Fact]
    public void Serie_ausente_esconde_o_resultado()
    {
        Assert.False(Eval("{FanSpeed#9} + 1", out _));
        Assert.False(Eval("max(1, {FanSpeed#9})", out _));
        Assert.False(Eval("-{FanSpeed#9}", out _));
        Assert.False(Eval("{CpuTemperature}", out _, new SensorData()));
    }

    [Fact]
    public void Numero_muito_grande_que_estoura_nao_produz_valor()
    {
        var huge = new string('9', 400);
        Assert.False(Eval(huge + "*" + huge, out _));
    }
}
