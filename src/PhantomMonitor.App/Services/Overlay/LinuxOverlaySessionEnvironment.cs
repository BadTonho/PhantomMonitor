using System.Diagnostics;
using System.Text;

namespace PhantomMonitor.App.Services.Overlay;

/// <summary>Publishes the Linux overlay and passive FPS-capture environment to the user session.</summary>
internal static class LinuxOverlaySessionEnvironment
{
    private const string EnvironmentFileName = "90-phantom-monitor-fps.conf";
    private const string LegacyEnvironmentFileName = "90-phantom-monitor-overlay.conf";
    private const string EnableVariable = "ENABLE_PHANTOM_OVERLAY";
    private const string DisableVariable = "DISABLE_PHANTOM_OVERLAY";
    private const string CaptureVariable = "PHANTOM_FPS_CAPTURE";
    private const string PreloadVariable = "LD_PRELOAD";

    private static string EnvironmentDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return !string.IsNullOrWhiteSpace(configured) && Path.IsPathRooted(configured)
                ? Path.Combine(configured, "environment.d")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "environment.d");
        }
    }

    private static string EnvironmentFile => Path.Combine(EnvironmentDirectory, EnvironmentFileName);
    private static string LegacyEnvironmentFile => Path.Combine(EnvironmentDirectory, LegacyEnvironmentFileName);

    public static bool HasLocalIntegrationArtifacts
    {
        get
        {
            if (!OperatingSystem.IsLinux()) return false;
            var processPreload = Environment.GetEnvironmentVariable(PreloadVariable) ?? "";
            return IsManagedEnvironmentFile()
                || IsManagedEnvironmentFile(LegacyEnvironmentFile)
                || File.Exists(LinuxOverlayNativeFiles.InstalledManifest)
                || ParsePreload(processPreload).Contains(LinuxOverlayNativeFiles.InstalledOpenGlLibrary, StringComparer.Ordinal);
        }
    }

    public static LinuxOverlaySessionResult Enable()
        => Configure(overlayEnabled: true);

    public static LinuxOverlaySessionResult EnableFpsCapture()
        => Configure(overlayEnabled: false);

    private static LinuxOverlaySessionResult Configure(bool overlayEnabled)
    {
        if (!OperatingSystem.IsLinux())
            return new(false, false, "A integração de ambiente da sessão está disponível somente no Linux.");

        if (!LinuxOverlayNativeFiles.EnsureInstalled(out var installMessage))
            return new(false, false, installMessage);

        try
        {
            if (LinuxOverlayNativeFiles.InstalledOpenGlLibrary.Any(char.IsWhiteSpace))
                return new(false, false,
                    "O caminho da biblioteca OpenGL contém espaços; o carregador dinâmico do Linux não consegue representar esse caminho em LD_PRELOAD. Vulkan ainda pode funcionar, mas a integração OpenGL foi cancelada.");

            var managerEnvironment = ReadUserManagerEnvironment();
            var processPreload = Environment.GetEnvironmentVariable(PreloadVariable) ?? "";
            var currentPreload = managerEnvironment.TryGetValue(PreloadVariable, out var managerPreload)
                ? CombinePreloads(managerPreload, processPreload)
                : processPreload;
            var preload = AddPreload(currentPreload, LinuxOverlayNativeFiles.InstalledOpenGlLibrary);
            var activationEnvironmentUpdated = TryUpdateDbusAndSystemd(preload, overlayEnabled, captureEnabled: true, out var updateError);
            var plasmaSession = LinuxDesktopLaunchEnvironment.IsPlasmaSession;
            var launchEnvironmentProvider = "";
            var launchEnvironmentError = "";
            var desktopLaunchEnvironmentUpdated = !plasmaSession
                || LinuxDesktopLaunchEnvironment.TryUpdate(
                    CreateLaunchEnvironment(preload, overlayEnabled, captureEnabled: true),
                    out launchEnvironmentProvider,
                    out launchEnvironmentError);

            if (activationEnvironmentUpdated)
            {
                try
                {
                    WritePersistentEnvironment(overlayEnabled, captureEnabled: true);
                    if (!desktopLaunchEnvironmentUpdated)
                    {
                        return new(false, true,
                            $"D-Bus/systemd foram atualizados e a configuração foi gravada para sessões futuras, mas nenhum serviço de lançamento do desktop confirmou o ambiente. Jogos abertos pelo desktop podem não receber o hook. {launchEnvironmentError}");
                    }

                    var verified = ReadUserManagerEnvironment();
                    if (verified.TryGetValue(PreloadVariable, out var verifiedPreload)
                        && string.Equals(verifiedPreload, preload, StringComparison.Ordinal)
                        && verified.TryGetValue(EnableVariable, out var enabledValue)
                        && enabledValue == (overlayEnabled ? "1" : "0")
                        && verified.TryGetValue(CaptureVariable, out var captureValue)
                        && captureValue == "1")
                    {
                        var launchManagerStatus = plasmaSession
                            ? $"{launchEnvironmentProvider}, D-Bus e systemd confirmaram o ambiente de lançamento"
                            : "D-Bus/systemd confirmaram as variáveis para serviços da sessão";
                        var purpose = overlayEnabled
                            ? "Camada registrada e overlay dentro do jogo habilitado"
                            : "Captura passiva de FPS EGL/Vulkan habilitada; o overlay dentro do jogo permanece desativado";
                        return new(true, true,
                            $"{purpose}; {launchManagerStatus}, e a configuração foi gravada para sessões futuras. Feche e reabra jogos ou launchers já abertos para que herdem o ambiente. Flatpak pode não herdar bibliotecas do host.");
                    }

                    return new(true, true,
                        "D-Bus aceitou as variáveis e a configuração foi gravada para sessões futuras, mas o systemd --user não confirmou. Feche e reabra jogos ou launchers já abertos; confira o estado do processo abaixo.");
                }
                catch (Exception ex)
                {
                    return new(true, false,
                        $"O ambiente D-Bus/systemd desta sessão foi atualizado, mas não foi possível gravar a integração para sessões futuras: {ex.Message}");
                }
            }

            if (plasmaSession && desktopLaunchEnvironmentUpdated)
            {
                try
                {
                    WritePersistentEnvironment(overlayEnabled, captureEnabled: true);
                    return new(true, true,
                        $"{launchEnvironmentProvider} confirmou o ambiente usado para iniciar aplicativos e a configuração foi gravada para sessões futuras, mas a atualização genérica de D-Bus/systemd falhou. {updateError}");
                }
                catch (Exception ex)
                {
                    return new(true, false,
                        $"{launchEnvironmentProvider} confirmou o ambiente usado para iniciar aplicativos, mas a configuração persistente não pôde ser gravada: {ex.Message}. {updateError}");
                }
            }

            if (updateError.StartsWith("O systemd --user foi atualizado", StringComparison.Ordinal))
            {
                try
                {
                    WritePersistentEnvironment(overlayEnabled, captureEnabled: true);
                    return new(false, true,
                        $"Integração parcial: o systemd --user foi atualizado e a configuração foi gravada para sessões futuras, mas o D-Bus não confirmou a atualização. A propagação para jogos iniciados pelo desktop não está confirmada. {updateError}");
                }
                catch (Exception ex)
                {
                    return new(false, false,
                        $"O systemd --user foi atualizado, mas D-Bus falhou e a configuração persistente não pôde ser gravada: {ex.Message}. {updateError}");
                }
            }

            return new(false, false,
                $"Não foi possível atualizar o ambiente de ativação D-Bus/systemd desta sessão. O hook não está confirmado para novos processos. {updateError}");
        }
        catch (Exception ex)
        {
            return new(false, false, $"Falha ao ativar a integração da sessão Linux: {ex.Message}");
        }
    }

    public static LinuxOverlaySessionResult Disable()
    {
        if (!OperatingSystem.IsLinux())
            return new(true, true, "Integração de sessão Linux não aplicável nesta plataforma.");

        if (!HasLocalIntegrationArtifacts)
            return new(true, true, "Nenhuma integração de sessão do PhantomMonitor está ativa.");

        var managerEnvironment = ReadUserManagerEnvironment();
        try
        {
            var processPreload = Environment.GetEnvironmentVariable(PreloadVariable) ?? "";
            var currentPreload = managerEnvironment.TryGetValue(PreloadVariable, out var activeManagerPreload)
                ? CombinePreloads(activeManagerPreload, processPreload)
                : processPreload;
            var preload = RemovePreload(currentPreload, LinuxOverlayNativeFiles.InstalledOpenGlLibrary);

            if (!TryUpdateDbusAndSystemd(preload, overlayEnabled: false, captureEnabled: false, out var updateError))
            {
                return new(false, false,
                    $"Não foi possível confirmar a desativação no ambiente D-Bus/systemd. O manifesto e a configuração persistente foram mantidos para permitir nova tentativa. Processos iniciados pelo shell gráfico podem manter o ambiente antigo. {updateError}");
            }

            if (LinuxDesktopLaunchEnvironment.IsPlasmaSession
                && !LinuxDesktopLaunchEnvironment.TryUpdate(
                    CreateLaunchEnvironment(preload, overlayEnabled: false, captureEnabled: false), out _, out var desktopLaunchError))
            {
                return new(false, false,
                    $"D-Bus/systemd foram atualizados, mas nenhum serviço de lançamento do desktop confirmou a desativação do ambiente. O manifesto e a configuração persistente foram mantidos. {desktopLaunchError}");
            }

            try
            {
                if (IsManagedEnvironmentFile())
                    File.Delete(EnvironmentFile);
                if (IsManagedEnvironmentFile(LegacyEnvironmentFile))
                    File.Delete(LegacyEnvironmentFile);
                RemoveEmptyEnvironmentDirectory();

                // The implicit manifest is owned by this app. Removing it prevents
                // new Vulkan processes from loading our layer in floating mode.
                LinuxOverlayNativeFiles.RemoveInstalledRegistration();
                return new(true, true, "Integração desativada para novos processos e removida das sessões futuras.");
            }
            catch (Exception ex)
            {
                return new(false, false,
                    $"O ambiente D-Bus/systemd foi desativado nesta sessão, mas não foi possível remover toda a configuração persistente: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            return new(false, false, $"Falha ao desativar a integração da sessão Linux: {ex.Message}");
        }
    }

    private static bool TryUpdateDbusAndSystemd(string preload, bool overlayEnabled, bool captureEnabled, out string error)
    {
        var arguments = new[]
        {
            "--systemd",
            $"{PreloadVariable}={preload}",
            $"{EnableVariable}={(overlayEnabled ? "1" : "0")}",
            $"{DisableVariable}=0",
            $"{CaptureVariable}={(captureEnabled ? "1" : "0")}" 
        };
        var dbusResult = Run("dbus-update-activation-environment", arguments);
        if (dbusResult.Started && dbusResult.ExitCode == 0)
        {
            error = "";
            return true;
        }

        // Some minimal desktops do not install the D-Bus helper. Updating systemd
        // still helps services started by the user manager, but is reported as partial.
        var systemdResult = Run("systemctl", ["--user", "set-environment", .. arguments.Skip(1)]);
        if (systemdResult.Started && systemdResult.ExitCode == 0)
        {
            error = "O systemd --user foi atualizado, mas o ambiente D-Bus não; a propagação a aplicativos do desktop não está confirmada.";
            return false;
        }

        var dbusDetail = FormatCommandFailure("dbus-update-activation-environment", dbusResult);
        var systemdDetail = FormatCommandFailure("systemctl --user set-environment", systemdResult);
        error = $"{dbusDetail} {systemdDetail}".Trim();
        return false;
    }

    private static Dictionary<string, string> CreateLaunchEnvironment(string preload, bool overlayEnabled, bool captureEnabled) => new(StringComparer.Ordinal)
    {
        [PreloadVariable] = preload,
        [EnableVariable] = overlayEnabled ? "1" : "0",
        [DisableVariable] = "0",
        [CaptureVariable] = captureEnabled ? "1" : "0"
    };

    private static Dictionary<string, string> ReadUserManagerEnvironment()
    {
        var result = Run("systemctl", ["--user", "show-environment"]);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!result.Started || result.ExitCode != 0) return values;

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            values[line[..separator]] = line[(separator + 1)..].TrimEnd('\r');
        }
        return values;
    }

    private static void WritePersistentEnvironment(bool overlayEnabled, bool captureEnabled)
    {
        if (File.Exists(EnvironmentFile) && !IsManagedEnvironmentFile())
            throw new IOException($"O arquivo {EnvironmentFile} já existe e não foi criado pelo PhantomMonitor; ele não foi sobrescrito.");
        if (File.Exists(LegacyEnvironmentFile))
        {
            if (!IsManagedEnvironmentFile(LegacyEnvironmentFile))
                throw new IOException($"O arquivo {LegacyEnvironmentFile} já existe e não foi criado pelo PhantomMonitor; ele não foi removido.");
            File.Delete(LegacyEnvironmentFile);
        }

        Directory.CreateDirectory(EnvironmentDirectory);
        var preload = EscapeEnvironmentValue(LinuxOverlayNativeFiles.InstalledOpenGlLibrary);
        var contents = new StringBuilder()
            .AppendLine("# Managed by PhantomMonitor. Remove this file to disable its Linux FPS and overlay integration.")
            .AppendLine($"{EnableVariable}={(overlayEnabled ? "1" : "0")}")
            .AppendLine($"{DisableVariable}=0")
            .AppendLine($"{CaptureVariable}={(captureEnabled ? "1" : "0")}")
            .Append(PreloadVariable).Append('=').Append('"').Append(preload)
            .Append("${LD_PRELOAD:+:$LD_PRELOAD}\"").AppendLine()
            .ToString();

        var temporary = EnvironmentFile + ".new-" + Environment.ProcessId;
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, EnvironmentFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool IsManagedEnvironmentFile() => IsManagedEnvironmentFile(EnvironmentFile);

    private static bool IsManagedEnvironmentFile(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var reader = new StreamReader(path);
            var marker = reader.ReadLine();
            return marker is "# Managed by PhantomMonitor. Remove this file to disable its Linux FPS and overlay integration."
                or "# Managed by PhantomMonitor. Remove this file to disable its Linux in-game overlay.";
        }
        catch
        {
            return false;
        }
    }

    private static string EscapeEnvironmentValue(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "$$", StringComparison.Ordinal);

    private static void RemoveEmptyEnvironmentDirectory()
    {
        if (Directory.Exists(EnvironmentDirectory) && !Directory.EnumerateFileSystemEntries(EnvironmentDirectory).Any())
            Directory.Delete(EnvironmentDirectory);
    }

    private static string AddPreload(string current, string path)
    {
        var entries = ParsePreload(current);
        if (!entries.Contains(path, StringComparer.Ordinal)) entries.Insert(0, path);
        return string.Join(Path.PathSeparator, entries);
    }

    private static string CombinePreloads(string first, string second)
        => string.Join(Path.PathSeparator, ParsePreload(first).Concat(ParsePreload(second)).Distinct(StringComparer.Ordinal));

    private static string RemovePreload(string current, string path)
        => string.Join(Path.PathSeparator, ParsePreload(current)
            .Where(entry => !string.Equals(entry, path, StringComparison.Ordinal)));

    private static List<string> ParsePreload(string value)
        => value.Split([':', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static CommandResult Run(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null) return new(false, -1, "", "o processo não iniciou");

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(4000))
            {
                process.Kill(entireProcessTree: true);
                return new(true, -1, stdout.GetAwaiter().GetResult(), "tempo limite excedido");
            }
            Task.WaitAll(stdout, stderr);
            return new(true, process.ExitCode, stdout.Result, stderr.Result.Trim());
        }
        catch (Exception ex)
        {
            return new(false, -1, "", ex.Message);
        }
    }

    private static string FormatCommandFailure(string command, CommandResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError;
        return $"{command}: {(detail.Length == 0 ? "falha ao executar" : detail)}";
    }

    private sealed record CommandResult(bool Started, int ExitCode, string StandardOutput, string StandardError);
}

internal sealed record LinuxOverlaySessionResult(bool SessionActive, bool Persistent, string Message);
