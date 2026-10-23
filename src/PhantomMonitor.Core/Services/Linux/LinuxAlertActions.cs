using PhantomMonitor.Models;

namespace PhantomMonitor.Services.Linux;

/// <summary>
/// Ações Linux para os disparos de alerta. O script roda com verificação de bit de
/// execução e argumentos fora de shell (<see cref="LinuxAlertScriptRunner"/>); a
/// notificação de desktop (DBus, lançada pelo frontend quando a janela está fora
/// de foco) carrega o próprio som — por isso <c>Sound</c> não emite beep de sistema
/// (uma notificação duplicaria o som, e tocar direto exigiria daemon específico).
/// O desligamento por alerta é Windows-only (sem equivalente seguro aqui).
/// </summary>
public sealed class LinuxAlertActions : IAlertActions
{
    public static LinuxAlertActions Shared { get; } = new();

    private readonly LinuxAlertScriptRunner _scripts = new();

    public void Sound()
    {
        // O som do alerta fica por conta da notificação de desktop (environment).
    }

    public void RunScript(AlertTrigger trigger) => _scripts.Run(trigger);

    public void Shutdown(string message) =>
        ErrorLog.Write("AlertActions", "Ação 'shutdown' ignorada: desligamento por alerta é exclusivo do Windows.");
}
