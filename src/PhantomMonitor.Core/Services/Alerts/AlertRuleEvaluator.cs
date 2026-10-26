using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>Avalia regras de alerta sem executar ações específicas de plataforma.</summary>
public sealed class AlertRuleEvaluator
{
    private readonly AlertsConfig _config;
    private readonly object _lock = new();
    private readonly Dictionary<AlertRule, DateTime> _lastTriggered = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AlertRule> _active = new(ReferenceEqualityComparer.Instance);

    public AlertRuleEvaluator(AlertsConfig config) => _config = config ?? new AlertsConfig();

    public IReadOnlyList<AlertTrigger> Evaluate(SensorData data)
    {
        lock (_lock)
        {
            return EvaluateCore(data);
        }
    }

    private IReadOnlyList<AlertTrigger> EvaluateCore(SensorData data)
    {
        if (!_config.Enabled || _config.Rules.Count == 0) return SafeEmpty;
        var triggered = new List<AlertTrigger>();

        foreach (var rule in _config.Rules)
        {
            if (rule is not { Enabled: true }) continue;

            var key = new SensorKey(rule.Sensor, rule.SensorIndex);
            bool present = key.IsPrimary
                ? data.Values.ContainsKey(rule.Sensor)
                : data.Series.ContainsKey(key);
            var activeKey = rule;

            if (!present)
            {
                _active.Remove(activeKey);
                continue;
            }

            double value = data.GetValue(key);
            bool condition = rule.Comparison == AlertComparison.Above
                ? value > rule.Threshold
                : value < rule.Threshold;

            if (!condition)
            {
                _active.Remove(activeKey);
                continue;
            }

            bool cooldownElapsed = !_lastTriggered.TryGetValue(activeKey, out var last)
                || (DateTime.UtcNow - last).TotalSeconds >= EffectiveCooldown(rule);

            if (!_active.Contains(activeKey) || cooldownElapsed)
            {
                _lastTriggered[activeKey] = DateTime.UtcNow;
                _active.Add(activeKey);
                string sensorName = key.ToString();
                string template = string.IsNullOrWhiteSpace(rule.Message)
                    ? "Sensor {sensor}: {value} (limite: {threshold})"
                    : rule.Message;
                string message = template
                    .Replace("{sensor}", sensorName)
                    .Replace("{value}", value.ToString("F1"))
                    .Replace("{threshold}", rule.Threshold.ToString("F1"));
                triggered.Add(new AlertTrigger(rule, value, sensorName, message));
            }
        }

        return triggered;
    }

    /// <summary>Lista compartilhada para o caminho desativado/sem regras: alocações
    /// por tick eram desperdício puro (o avaliador roda a cada snapshot).</summary>
    private static readonly IReadOnlyList<AlertTrigger> SafeEmpty = [];

    private int EffectiveCooldown(AlertRule rule) =>
        rule.CooldownSeconds > 0 ? rule.CooldownSeconds : Math.Max(1, _config.CooldownSeconds);
}
