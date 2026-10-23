using PhantomMonitor.Services.Linux;

namespace PhantomMonitor.Services;

/// <summary>
/// Instância única: duas cópias do app disputariam a mesma sessão ETW do
/// PresentMon (<c>--stop_existing_session</c> mata a sessão da outra) e o mesmo
/// <c>sensors_latest.json</c>. Mutex nomeado no Windows (paridade do WPF,
/// <c>Local\PhantomMonitor.SingleInstance</c>) e lock exclusivo de arquivo no Linux.
/// </summary>
public static class SingleInstanceGuard
{
    /// <summary>Adquire a posse única; <c>null</c> = outra instância está em execução (o chamador encerra).</summary>
    public static IDisposable? TryAcquire() =>
        OperatingSystem.IsWindows() ? WindowsSingleInstanceGuard.TryAcquire() : LinuxSingleInstanceGuard.TryAcquire();
}
