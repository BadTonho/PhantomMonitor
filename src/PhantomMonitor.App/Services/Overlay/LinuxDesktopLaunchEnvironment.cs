using System.Diagnostics;

namespace PhantomMonitor.App.Services.Overlay;

/// <summary>Updates launch environments exposed by KDE Plasma/KLauncher.</summary>
internal static class LinuxDesktopLaunchEnvironment
{
    private static readonly LaunchEnvironmentApi[] LaunchEnvironmentApis =
    [
        new("Plasma", "org.kde.Startup", "/Startup", "org.kde.Startup", "updateLaunchEnv"),
        new("KLauncher", "org.kde.klauncher", "/KLauncher", "org.kde.KLauncher", "setLaunchEnv"),
        new("KLauncher 5", "org.kde.klauncher5", "/KLauncher", "org.kde.KLauncher", "setLaunchEnv")
    ];

    internal static bool IsPlasmaSession
    {
        get
        {
            static bool HasPlasmaToken(string? value) => !string.IsNullOrWhiteSpace(value)
                && value.Split([':', ';'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(token => token.Contains("KDE", StringComparison.OrdinalIgnoreCase)
                        || token.Contains("Plasma", StringComparison.OrdinalIgnoreCase));

            return HasPlasmaToken(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"))
                || HasPlasmaToken(Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP"))
                || HasPlasmaToken(Environment.GetEnvironmentVariable("DESKTOP_SESSION"))
                || string.Equals(Environment.GetEnvironmentVariable("KDE_FULL_SESSION"), "true", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KDE_SESSION_VERSION"));
        }
    }

    private static bool IsPlasma6Session
        => (Environment.GetEnvironmentVariable("KDE_SESSION_VERSION") ?? "")
            .StartsWith("6", StringComparison.Ordinal);

    internal static bool TryUpdate(
        IReadOnlyDictionary<string, string> variables,
        out string provider,
        out string error)
    {
        var failures = new List<string>();
        foreach (var api in LaunchEnvironmentApis.Where(api => !IsPlasma6Session || api.Name != "KLauncher 5"))
        {
            if (TryUpdate(api, variables, out var apiError))
            {
                provider = api.Name;
                error = "";
                return true;
            }

            failures.Add($"{api.Name}: {apiError}");
        }

        provider = "";
        error = string.Join("; ", failures);
        return false;
    }

    private static bool TryUpdate(
        LaunchEnvironmentApi api,
        IReadOnlyDictionary<string, string> variables,
        out string error)
    {
        foreach (var (name, value) in variables)
        {
            var result = Run("dbus-send", [
                "--session",
                "--print-reply=literal",
                $"--dest={api.Service}",
                api.ObjectPath,
                $"{api.Interface}.{api.Method}",
                $"string:{name}",
                $"string:{value}"
            ]);

            if (result.Started && result.ExitCode == 0) continue;

            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput.Trim()
                : result.StandardError;
            error = $"não confirmou {name}: {(detail.Length == 0 ? "falha ao executar dbus-send" : detail)}";
            return false;
        }

        error = "";
        return true;
    }

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

    private sealed record CommandResult(bool Started, int ExitCode, string StandardOutput, string StandardError);

    private sealed record LaunchEnvironmentApi(
        string Name,
        string Service,
        string ObjectPath,
        string Interface,
        string Method);
}
