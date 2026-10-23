using PhantomMonitor.Services.Linux;

namespace PhantomMonitor.Services;

/// <summary>Escolhe as ações por SO (mesmo padrão da <c>SensorServiceFactory</c>).</summary>
public static class AlertActionFactory
{
    public static IAlertActions Create() =>
        OperatingSystem.IsWindows() ? WindowsAlertActions.Shared : LinuxAlertActions.Shared;
}
