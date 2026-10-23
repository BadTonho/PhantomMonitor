namespace PhantomMonitor.Models;

/// <summary>Uma regra que entrou ou permaneceu ativa após o cooldown.</summary>
public sealed record AlertTrigger(AlertRule Rule, double Value, string SensorName, string Message);
