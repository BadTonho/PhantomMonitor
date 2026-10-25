namespace PhantomMonitor.Services;

public sealed record PresentingProcess(int ProcessId, string Application, DateTimeOffset LastPresentationUtc);

/// <summary>Optional sensor-service capability for applications presenting frames.</summary>
public interface IOverlayProcessDiscovery
{
    bool OverlayProcessDiscoveryEnabled { get; set; }
    IReadOnlyList<PresentingProcess> GetRecentPresentingProcesses();
    string OverlayDiscoveryDiagnostic { get; }
}
