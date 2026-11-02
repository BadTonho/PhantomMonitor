using PhantomMonitor.Models;
using PhantomMonitor.Services;

/// <summary>Testes de lógica pura de <see cref="AlertRuleEvaluator"/> (borda, cooldown, regras duplicadas).</summary>
public sealed class AlertRuleEvaluatorTests
{
    private static SensorData Cpu(double temperature)
    {
        var data = new SensorData();
        data.SetValue(SensorType.CpuTemperature, temperature);
        return data;
    }

    private static AlertRuleEvaluator Evaluator(params AlertRule[] rules) =>
        new(new AlertsConfig { Enabled = true, Rules = [.. rules] });

    [Fact]
    public void Config_nula_ou_desativada_nao_dispara()
    {
        Assert.Empty(new AlertRuleEvaluator(null!).Evaluate(Cpu(100)));
        var disabled = new AlertRuleEvaluator(new AlertsConfig { Enabled = false, Rules = [new() { Threshold = 10 }] });
        Assert.Empty(disabled.Evaluate(Cpu(100)));
        Assert.Empty(Evaluator().Evaluate(Cpu(100)));
    }

    [Fact]
    public void Regra_desativada_ou_nula_e_ignorada()
    {
        var evaluator = Evaluator(new AlertRule { Enabled = false, Threshold = 10 }, null!);
        Assert.Empty(evaluator.Evaluate(Cpu(100)));
    }

    [Theory]
    [InlineData(AlertComparison.Above, 80, 80.0001, true)]
    [InlineData(AlertComparison.Above, 80, 80, false)]
    [InlineData(AlertComparison.Above, 80, 79, false)]
    [InlineData(AlertComparison.Below, 20, 19.9, true)]
    [InlineData(AlertComparison.Below, 20, 20, false)]
    [InlineData(AlertComparison.Below, 20, 21, false)]
    public void Comparacao_e_estrita(AlertComparison comparison, double threshold, double value, bool fires)
    {
        var evaluator = Evaluator(new AlertRule { Comparison = comparison, Threshold = threshold });
        Assert.Equal(fires, evaluator.Evaluate(Cpu(value)).Count == 1);
    }

    [Fact]
    public void Limiar_ou_valor_nan_nunca_dispara()
    {
        Assert.Empty(Evaluator(new AlertRule { Threshold = double.NaN }).Evaluate(Cpu(100)));
        Assert.Empty(Evaluator(new AlertRule { Threshold = 10 }).Evaluate(Cpu(double.NaN)));
    }

    [Fact]
    public void Dispara_na_borda_e_respeita_cooldown_enquanto_a_condicao_persiste()
    {
        var evaluator = Evaluator(new AlertRule { Threshold = 80, CooldownSeconds = 3600 });
        Assert.Single(evaluator.Evaluate(Cpu(90)));
        Assert.Empty(evaluator.Evaluate(Cpu(95)));
        Assert.Empty(evaluator.Evaluate(Cpu(99)));
    }

    [Fact]
    public void Voltar_a_condicao_falsa_rearma_a_regra()
    {
        var evaluator = Evaluator(new AlertRule { Threshold = 80, CooldownSeconds = 3600 });
        Assert.Single(evaluator.Evaluate(Cpu(90)));
        Assert.Empty(evaluator.Evaluate(Cpu(70)));
        Assert.Single(evaluator.Evaluate(Cpu(90)));
    }

    [Fact]
    public void Sensor_ausente_rearma_a_regra()
    {
        var evaluator = Evaluator(new AlertRule { Threshold = 80, CooldownSeconds = 3600 });
        Assert.Single(evaluator.Evaluate(Cpu(90)));
        Assert.Empty(evaluator.Evaluate(new SensorData()));
        Assert.Single(evaluator.Evaluate(Cpu(90)));
    }

    [Fact]
    public void Regras_duplicadas_tem_estado_e_acoes_independentes()
    {
        var first = new AlertRule { Threshold = 80, Popup = true, CooldownSeconds = 3600 };
        var second = new AlertRule { Threshold = 80, Popup = false, Script = "script", CooldownSeconds = 3600 };
        var evaluator = Evaluator(first, second);
        var triggers = evaluator.Evaluate(Cpu(90));
        Assert.Equal(2, triggers.Count);
        Assert.Same(first, triggers[0].Rule);
        Assert.Same(second, triggers[1].Rule);
        Assert.Empty(evaluator.Evaluate(Cpu(90)));
    }

    [Fact]
    public void Mesma_instancia_de_regra_listada_duas_vezes_e_avaliada_uma_vez_por_ciclo_de_estado()
    {
        var rule = new AlertRule { Threshold = 80, CooldownSeconds = 3600 };
        var evaluator = Evaluator(rule, rule);
        // A segunda ocorrência encontra a regra já ativa e dentro do cooldown.
        Assert.Single(evaluator.Evaluate(Cpu(90)));
    }

    [Fact]
    public void Serie_por_instancia_exige_a_serie_exata()
    {
        var rule = new AlertRule { Sensor = SensorType.GpuTemperature, SensorIndex = 2, Threshold = 70 };
        var evaluator = Evaluator(rule);

        var onlyPrimary = new SensorData();
        onlyPrimary.SetValue(SensorType.GpuTemperature, 95);
        Assert.Empty(evaluator.Evaluate(onlyPrimary));

        var instance = new SensorData();
        instance.SetValue(new SensorKey(SensorType.GpuTemperature, 2), 75);
        var trigger = Assert.Single(evaluator.Evaluate(instance));
        Assert.Equal("GpuTemperature#2", trigger.SensorName);
        Assert.Equal(75, trigger.Value);
    }

    [Fact]
    public void Indice_negativo_equivale_a_serie_canonica()
    {
        var evaluator = Evaluator(new AlertRule { SensorIndex = -3, Threshold = 50 });
        Assert.Single(evaluator.Evaluate(Cpu(60)));
    }

    [Fact]
    public void Mensagem_padrao_e_personalizada_substituem_placeholders()
    {
        var defaultRule = new AlertRule { Threshold = 80, Message = "  " };
        var custom = new AlertRule { Threshold = 80, Message = "{sensor}/{value}/{threshold}/{sensor}" };
        var triggers = Evaluator(defaultRule, custom).Evaluate(Cpu(90));
        Assert.Contains("CpuTemperature", triggers[0].Message);
        Assert.DoesNotContain("{", triggers[0].Message);
        Assert.StartsWith("CpuTemperature/", triggers[1].Message);
        Assert.EndsWith("/CpuTemperature", triggers[1].Message);
        Assert.DoesNotContain("{", triggers[1].Message);
    }

    [Fact]
    public void Cooldown_efetivo_da_regra_zero_usa_o_global_minimo_de_1_segundo()
    {
        // Cooldown global 0 (inválido, sem Normalize) cai para 1s: ainda suprime ticks imediatos.
        var evaluator = new AlertRuleEvaluator(new AlertsConfig
        {
            Enabled = true, CooldownSeconds = 0, Rules = [new AlertRule { Threshold = 80 }]
        });
        Assert.Single(evaluator.Evaluate(Cpu(90)));
        Assert.Empty(evaluator.Evaluate(Cpu(90)));
    }

    [Fact]
    public void Dispara_novamente_apos_o_cooldown_decorrer()
    {
        var evaluator = Evaluator(new AlertRule { Threshold = 80, CooldownSeconds = 1 });
        Assert.Single(evaluator.Evaluate(Cpu(90)));
        Thread.Sleep(1100);
        Assert.Single(evaluator.Evaluate(Cpu(90)));
    }
}
