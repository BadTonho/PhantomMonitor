namespace PhantomMonitor.Services;

/// <summary>
/// Inicialização automática com o SO: registro <c>HKCU\...\Run</c> no Windows
/// (paridade do StartupManager do WPF) e entrada XDG autostart no Linux.
/// </summary>
public interface IStartupManager
{
    /// <summary>Reflete o estado real do registro/configuração do SO, não a última chamada.</summary>
    bool IsEnabled();

    bool SetEnabled(bool enabled);
}
