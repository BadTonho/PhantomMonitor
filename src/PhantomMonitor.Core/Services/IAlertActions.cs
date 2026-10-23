using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Ações de sistema disparadas por alertas — a orquestração (ordem popup &gt; som &gt;
/// script &gt; shutdown + guard) vive no ViewModel do frontend; as implementações por
/// SO (<see cref="WindowsAlertActions"/> / <see cref="LinuxAlertActions"/>) executam
/// o que for possível. A parte de UI (toast/popup interno e notificação de desktop
/// fora de foco) fica no frontend, como no WPF.
/// </summary>
public interface IAlertActions
{
    /// <summary>Beep de sistema (no Windows, <c>MessageBeep</c> com ícone de exclamação —
    /// paridade do AlertService; no Linux a notificação de desktop emite o próprio som).</summary>
    void Sound();

    /// <summary>Executa o script da regra com <c>{value}</c>/<c>{sensor}</c> substituídos.</summary>
    void RunScript(AlertTrigger trigger);

    /// <summary>Desligamento agendado (Windows: <c>shutdown /s /t 60</c> com a mensagem como
    /// comentário). No Linux não é executado (paridade do suporte atual: preservado, não executado).</summary>
    void Shutdown(string message);
}
