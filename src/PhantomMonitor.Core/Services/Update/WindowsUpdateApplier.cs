using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace PhantomMonitor.Services.Update;

/// <summary>Stages the entire portable payload and launches a transactional PowerShell updater.</summary>
public sealed class WindowsUpdateApplier : IUpdateApplier
{
    public static WindowsUpdateApplier Shared { get; } = new();
    public string AssetNameForVersion(string version) => $"PhantomMonitor-{version}-win-x64.zip";
    public bool CanApply => UpdatePayload.CanWriteDirectory(AppContext.BaseDirectory);

    public async Task<string> PrepareAsync(GitHubReleaseAsset asset, string version, Action<double>? progress, CancellationToken cancellation)
    {
        var root = UserDataPaths.UpdateStagingDirectory;
        var directory = UpdatePayload.VersionDirectory(root, version);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        Directory.CreateDirectory(directory);
        var zip = Path.Combine(root, AssetNameForVersion(version));
        await GitHubReleaseSource.DownloadFileAsync(asset, zip, progress, cancellation).ConfigureAwait(false);
        await ExtractZipAsync(zip, directory, cancellation).ConfigureAwait(false);
        UpdatePayload.ValidateDirectory(directory, "PhantomMonitor.exe");
        File.Delete(zip);
        File.WriteAllText(Path.Combine(root, "apply_update.ps1"), UpdatePayload.ReadScript("windows.ps1"), new UTF8Encoding(true));
        return version;
    }

    public void Apply(string version)
    {
        var root = UserDataPaths.UpdateStagingDirectory;
        var directory = UpdatePayload.VersionDirectory(root, version);
        var script = Path.Combine(root, "apply_update.ps1");
        UpdatePayload.ValidateDirectory(directory, "PhantomMonitor.exe");
        if (!File.Exists(script)) throw new IOException("Script de atualização ausente; prepare novamente.");
        // The app releases its exe and PresentMon during shutdown. Hook DLLs in
        // other processes must be released before requesting that shutdown.
        var native = Path.Combine(AppContext.BaseDirectory, "PhantomOverlay");
        if (Directory.Exists(native))
            foreach (var file in Directory.EnumerateFiles(native, "*", SearchOption.AllDirectories))
                using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        var start = new ProcessStartInfo("powershell.exe")
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
        start.Environment["PHANTOM_UPDATE_PID"] = Environment.ProcessId.ToString();
        start.Environment["PHANTOM_UPDATE_PAYLOAD"] = directory;
        start.Environment["PHANTOM_UPDATE_TARGET"] = AppContext.BaseDirectory;
        start.Environment["PHANTOM_UPDATE_EXE"] = "PhantomMonitor.exe";
        start.Environment["PHANTOM_UPDATE_INSTALLED"] = ConfigPaths.IsPortable ? "0" : "1";
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o atualizador.");
    }

    internal static async Task ExtractZipAsync(string path, string directory, CancellationToken cancellation)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(directory, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Caminho fora do pacote.");
            if (entry.FullName.Contains(':') || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new IOException("Link ou nome de arquivo inválido no pacote.");
            if (entry.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = File.Create(target);
            await input.CopyToAsync(output, cancellation).ConfigureAwait(false);
        }
    }
}
