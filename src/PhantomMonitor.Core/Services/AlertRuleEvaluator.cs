using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>Avalia regras de alerta sem executar ações específicas de plataforma.</summary>
public sealed class AlertRuleEvaluator
{
    private readonly AlertsConfig _config;
    private readonly Dictionary<int, DateTime> _lastTriggered = new();
    private readonly HashSet<int> _active = new();

    public AlertRuleEvaluator(AlertsConfig config) => _config = config ?? new AlertsConfig();

    public IReadOnlyList<AlertTrigger> Evaluate(SensorData data)
    {
        var triggered = new List<AlertTrigger>();
        if (!_config.Enabled || _config.Rules.Count == 0) return triggered;

        foreach (var rule in _config.Rules)
        {
            if (rule is not { Enabled: true }) continue;

            var key = new SensorKey(rule.Sensor, rule.SensorIndex);
            bool present = key.IsPrimary
                ? data.Values.ContainsKey(rule.Sensor)
                : data.Series.ContainsKey(key);
            int activeKey = ActiveKey(rule);

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

    private static int ActiveKey(AlertRule rule) =>
        HashCode.Combine(rule.Sensor, rule.SensorIndex, rule.Comparison, rule.Threshold, rule.Message, rule.CooldownSeconds);

    private int EffectiveCooldown(AlertRule rule) =>
        rule.CooldownSeconds > 0 ? rule.CooldownSeconds : Math.Max(1, _config.CooldownSeconds);
}
