using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PhantomMonitor.App.Services.Overlay;

/// <summary>Runs the architecture-matched, allowlist-directed native overlay launcher.</summary>
internal static class WindowsOverlayHookService
{
    internal sealed record Result(bool Success, string Message, bool TargetStarted = false);
    private const uint ProcessQueryLimitedInformation = 0x1000;

    internal static bool TryGetExecutablePath(int processId, out string executablePath)
    {
        executablePath = "";
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return false;
        try
        {
            var buffer = new StringBuilder(32768);
            var length = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(process, 0, buffer, ref length)) return false;
            executablePath = buffer.ToString();
            return executablePath.Length > 0;
        }
        finally { CloseHandle(process); }
    }

    internal static async Task<Result> LaunchAsync(string executablePath, string arguments)
    {
        if (!TryResolve(executablePath, out var launcher, out var library, out var error))
            return new Result(false, error);
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var layerDirectory = Path.GetDirectoryName(launcher)!;
        if (File.Exists(Path.Combine(layerDirectory, "VkLayer_phantom_monitor.dll"))
            && File.Exists(Path.Combine(layerDirectory, "phantom_vulkan_layer.json")))
        {
            environment["VK_LAYER_PATH"] = AppendEnvironmentPath("VK_LAYER_PATH", layerDirectory, ";");
            environment["VK_INSTANCE_LAYERS"] = AppendLayer("VK_INSTANCE_LAYERS", "VK_LAYER_PHANTOM_monitor", ";");
            environment["ENABLE_PHANTOM_OVERLAY"] = "1";
        }
        return await RunAsync(launcher, ["--launch", library, executablePath, arguments], environment);
    }

    internal static async Task<Result> InjectAsync(int processId, string executablePath)
    {
        if (!TryResolve(executablePath, out var launcher, out var library, out var error))
            return new Result(false, error);
        return await RunAsync(launcher, ["--inject", processId.ToString(), library]);
    }

    private static bool TryResolve(string executablePath, out string launcher, out string library, out string error)
    {
        launcher = library = error = "";
        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            if (!File.Exists(fullPath)) { error = "O executável cadastrado não existe mais."; return false; }
            var architecture = ReadArchitecture(fullPath);
            if (architecture is null)
            {
                error = "Arquitetura do executável não suportada; o hook oferece somente x86 e x64.";
                return false;
            }
            var nativeDirectory = Path.Combine(AppContext.BaseDirectory, "PhantomOverlay", architecture);
            launcher = Path.Combine(nativeDirectory, "PhantomOverlayLauncher.exe");
            library = Path.Combine(nativeDirectory, "PhantomOverlayHook.dll");
            if (!File.Exists(launcher) || !File.Exists(library))
            {
                error = $"Arquivos nativos do overlay {architecture} não encontrados em {nativeDirectory}. Compile e instale os artefatos Windows do PhantomOverlayNative.";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException or OverflowException)
        {
            error = $"Não foi possível validar o executável: {ex.Message}";
            return false;
        }
    }

    private static string? ReadArchitecture(string executablePath)
    {
        using var stream = File.OpenRead(executablePath);
        if (stream.Length < 0x40) return null;
        stream.Position = 0x3c;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var peOffset = reader.ReadInt32();
        if (peOffset < 0 || peOffset > stream.Length - 6) return null;
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550) return null;
        return reader.ReadUInt16() switch
        {
            0x014c => "x86",
            0x8664 => "x64",
            _ => null
        };
    }

    private static string AppendEnvironmentPath(string name, string item, string separator)
    {
        var existing = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(existing) ? item : item + separator + existing;
    }

    private static string AppendLayer(string name, string layer, string separator)
    {
        var existing = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(existing)) return layer;
        return existing.Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Contains(layer, StringComparer.OrdinalIgnoreCase) ? existing : existing + separator + layer;
    }

    private static async Task<Result> RunAsync(string launcher, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launcher,
                WorkingDirectory = Path.GetDirectoryName(launcher) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (environment is not null)
                foreach (var item in environment) startInfo.Environment[item.Key] = item.Value;
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null) return new Result(false, "Não foi possível iniciar o launcher nativo.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            var detail = string.Join(" ", new[] { output.Trim(), error.Trim() }.Where(text => text.Length > 0));
            return process.ExitCode == 0
                ? new Result(true, detail.Length == 0 ? "Hook instalado." : detail)
                : new Result(false, detail.Length == 0 ? $"Launcher nativo terminou com código {process.ExitCode}." : detail,
                    TargetStarted: process.ExitCode == 4);
        }
        catch (TimeoutException)
        {
            return new Result(false, "O launcher nativo excedeu o tempo limite.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                   or Win32Exception or ArgumentException or NotSupportedException)
        {
            return new Result(false, $"Falha no launcher nativo: {ex.Message}");
        }
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags,
        StringBuilder executableName, ref uint size);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
