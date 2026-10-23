using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Ações Windows para os disparos de alerta — paridade integral do
/// <c>AlertService</c> do WPF: <c>MessageBeep</c> (aviso de sistema), script por
/// associação de arquivo (<c>UseShellExecute</c>, suportando .exe/.bat/.cmd/.ps1)
/// e desligamento agendado em 60 s. O guard <c>allowShutdown</c> é avaliado pelo
/// orquestrador (mensagem idêntica à do WPF).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAlertActions : IAlertActions
{
    public static WindowsAlertActions Shared { get; } = new();

    public void Sound() => MessageBeep(0x00000030);

    public void RunScript(AlertTrigger trigger)
    {
        var rule = trigger.Rule;
        if (string.IsNullOrWhiteSpace(rule.Script)) return;

        if (!File.Exists(rule.Script))
        {
            ErrorLog.Write("AlertActions", $"Script do alerta não encontrado: {rule.Script}");
            return;
        }

        var args = (rule.ScriptArguments ?? "")
            .Replace("{value}", trigger.Value.ToString("F1", CultureInfo.InvariantCulture))
            .Replace("{sensor}", trigger.SensorName);

        Process.Start(new ProcessStartInfo
        {
            FileName = rule.Script,
            Arguments = args,
            UseShellExecute = true
        })?.Dispose();
    }

    public void Shutdown(string message)
    {
        var comment = message.Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ');
        if (comment.Length > 400) comment = comment[..400];

        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = $"/s /t 60 /c \"{comment}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        })?.Dispose();

        ErrorLog.Write("AlertActions", "Desligamento agendado para daqui a 60 s (cancele com 'shutdown /a').");
    }

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);
}
