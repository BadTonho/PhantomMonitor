using System.Diagnostics;
using System.Globalization;
using System.Text;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services.Linux;

/// <summary>
/// Inicia scripts de alerta no Linux sem passar argumentos por shell: .sh via
/// /bin/sh; outros arquivos precisam do bit de execução. Move a pergunta de
/// App -&gt; Core na F1.5 (com a orquestração por SO), mesmo contrato do WPF.
/// </summary>
public sealed class LinuxAlertScriptRunner
{
    public void Run(AlertTrigger trigger)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(trigger.Rule.Script))
            return;

        _ = Task.Run(() => StartProcess(trigger));
    }

    private static void StartProcess(AlertTrigger trigger)
    {
        var configuredPath = trigger.Rule.Script.Trim();
        try
        {
            var scriptPath = Path.GetFullPath(configuredPath);
            if (!File.Exists(scriptPath))
            {
                ErrorLog.Write("LinuxAlertScript", $"Script do alerta não encontrado: {scriptPath}");
                return;
            }

            var isShellScript = string.Equals(Path.GetExtension(scriptPath), ".sh", StringComparison.OrdinalIgnoreCase);
            if (!isShellScript && !IsExecutable(scriptPath))
            {
                ErrorLog.Write("LinuxAlertScript", $"O arquivo do alerta não é executável: {scriptPath}");
                return;
            }

            var argumentsText = (trigger.Rule.ScriptArguments ?? "")
                .Replace("{value}", trigger.Value.ToString("F1", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{sensor}", trigger.SensorName, StringComparison.Ordinal);
            var arguments = ParseArguments(argumentsText);
            var startInfo = new ProcessStartInfo
            {
                FileName = isShellScript ? "/bin/sh" : scriptPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (isShellScript)
                startInfo.ArgumentList.Add(scriptPath);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                ErrorLog.Write("LinuxAlertScript", $"Não foi possível iniciar o script: {scriptPath}");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxAlertScript", $"Falha ao executar '{configuredPath}': {ex.Message}");
        }
    }

    private static bool IsExecutable(string path)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        var mode = File.GetUnixFileMode(path);
        const UnixFileMode executeBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (mode & executeBits) != 0;
    }

    private static IReadOnlyList<string> ParseArguments(string value)
    {
        var result = new List<string>();
        var argument = new StringBuilder();
        char? quote = null;
        var escaped = false;
        var argumentStarted = false;

        foreach (var character in value)
        {
            if (escaped)
            {
                argument.Append(character);
                escaped = false;
                argumentStarted = true;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                argumentStarted = true;
                continue;
            }

            if (quote.HasValue)
            {
                if (character == quote.Value)
                    quote = null;
                else
                    argument.Append(character);
                argumentStarted = true;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                argumentStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (argumentStarted)
                {
                    result.Add(argument.ToString());
                    argument.Clear();
                    argumentStarted = false;
                }
                continue;
            }

            argument.Append(character);
            argumentStarted = true;
        }

        if (escaped)
            throw new FormatException("A barra invertida final não escapa nenhum caractere.");
        if (quote.HasValue)
            throw new FormatException("Há uma aspa sem fechamento nos argumentos do script.");
        if (argumentStarted)
            result.Add(argument.ToString());

        return result;
    }
}
