namespace PhantomMonitor.Models;

/// <summary>
/// Estado atual de uma métrica opcional, separado dos erros de execução do aplicativo.
/// Metric nulo representa um aviso geral, por exemplo quando nenhuma GPU foi enumerada.
/// </summary>
public sealed record SensorAvailabilityDiagnostic(
    string Device,
    SensorType? Metric,
    bool IsAvailable,
    string Source,
    string Reason);
