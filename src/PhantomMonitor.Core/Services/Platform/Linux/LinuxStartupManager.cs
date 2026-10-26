using System.IO;
using System.Text;

namespace PhantomMonitor.Services.Linux;

/// <summary>
/// Inicialização automática no Linux via entrada XDG autostart
/// (<c>~/.config/autostart/phantom-monitor.desktop</c>): o <c>Exec</c> aponta para o
/// executável atual e <see cref="IsEnabled"/> valida que a entrada continua casando
/// com ele — mesma semântica da comparação de caminho que o registro Run faz no Windows.
/// </summary>
public sealed class LinuxStartupManager : IStartupManager
{
    public static LinuxStartupManager Shared { get; } = new();

    private static string AutostartDirectory => Path.Combine(ResolveConfigRoot(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "autostart");

    internal static string ResolveConfigRoot(string? configured, string home) =>
        !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? configured : Path.Combine(home, ".config");

    internal static string? ResolveExecutable(string? appImage, string? processPath) =>
        !string.IsNullOrWhiteSpace(appImage) && Path.IsPathFullyQualified(appImage) && File.Exists(appImage)
            ? appImage : processPath;

    private static string? Executable => ResolveExecutable(Environment.GetEnvironmentVariable("APPIMAGE"), Environment.ProcessPath);

    internal static string FormatExec(string path)
    {
        if (path.Any(char.IsControl)) throw new ArgumentException("O caminho do executável contém caracteres de controle.");
        // Exec quoting followed by Desktop Entry string escaping; no shell.
        var quoted = path.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal);
        return "\"" + quoted.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"";
    }

    private static string DesktopFilePath => Path.Combine(AutostartDirectory, "phantom-monitor.desktop");

    public bool IsEnabled()
    {
        try
        {
            var exe = Executable;
            if (string.IsNullOrEmpty(exe)) return false;

            if (!File.Exists(DesktopFilePath)) return false;
            foreach (var line in File.ReadAllLines(DesktopFilePath))
            {
                if (!line.StartsWith("Exec=", StringComparison.Ordinal)) continue;

                // A entrada escrita aqui é Exec="<exe>" (sem argumentos); compara sem aspas.
                var registered = line["Exec=".Length..].Trim();
                return string.Equals(registered, FormatExec(exe), StringComparison.Ordinal);
            }
            return false;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxStartupManager.IsEnabled", ex);
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(DesktopFilePath)) File.Delete(DesktopFilePath);
                return true;
            }

            var exe = Executable;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;

            Directory.CreateDirectory(AutostartDirectory);
            var desktop =
                $"""
                [Desktop Entry]
                Type=Application
                Name=Phantom Monitor
                Comment=Monitor de hardware ao vivo com OSD, alertas e gravação
                Exec={FormatExec(exe)}
                Terminal=false
                Categories=System;Utility;
                """;
            File.WriteAllText(DesktopFilePath, desktop, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxStartupManager.SetEnabled", ex);
            return false;
        }
    }
}
