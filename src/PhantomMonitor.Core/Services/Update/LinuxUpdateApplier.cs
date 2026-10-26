using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace PhantomMonitor.Services.Update;

/// <summary>Stages portable/AppImage releases and starts a POSIX transactional updater.</summary>
public sealed class LinuxUpdateApplier : IUpdateApplier
{
    public static LinuxUpdateApplier Shared { get; } = new();
    private readonly string? _appImagePath;
    private bool IsAppImage => _appImagePath is not null;

    public LinuxUpdateApplier()
    {
        var path = Environment.GetEnvironmentVariable("APPIMAGE");
        _appImagePath = !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path) ? path : null;
    }

    public string AssetNameForVersion(string version) => IsAppImage
        ? $"PhantomMonitor-{version}-linux-x64.AppImage" : $"PhantomMonitor-{version}-linux-x64.tar.gz";
    public bool CanApply => UpdatePayload.CanWriteDirectory(IsAppImage ? Path.GetDirectoryName(_appImagePath!)! : AppContext.BaseDirectory);
    internal string PayloadPath(string root, string version) => Path.Combine(UpdatePayload.VersionDirectory(root, version), AssetNameForVersion(version));

    public async Task<string> PrepareAsync(GitHubReleaseAsset asset, string version, Action<double>? progress, CancellationToken cancellation)
    {
        var root = UserDataPaths.UpdateStagingDirectory;
        var directory = UpdatePayload.VersionDirectory(root, version);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        Directory.CreateDirectory(directory);
        var path = PayloadPath(root, version);
        await GitHubReleaseSource.DownloadFileAsync(asset, path, progress, cancellation).ConfigureAwait(false);
        if (!IsAppImage)
        {
            // Extraction does not interpolate paths through a shell.
            await ExtractPortableAsync(path, directory, cancellation).ConfigureAwait(false);
            File.Delete(path);
            UpdatePayload.ValidateDirectory(directory, "PhantomMonitor");
        }
        File.WriteAllText(Path.Combine(root, "apply_update.sh"), UpdatePayload.ReadScript("linux.sh").Replace("\r\n", "\n"), new UTF8Encoding(false));
        return version;
    }

    internal static async Task ExtractPortableAsync(string path, string directory, CancellationToken cancellation)
    {
        await using var input = File.OpenRead(path);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, directory, overwriteFiles: true, cancellation).ConfigureAwait(false);
    }

    public void Apply(string version)
    {
        var root = UserDataPaths.UpdateStagingDirectory;
        var script = Path.Combine(root, "apply_update.sh");
        if (!File.Exists(script)) throw new IOException("Script de atualização ausente; prepare novamente.");
        var payload = IsAppImage ? PayloadPath(root, version) : UpdatePayload.VersionDirectory(root, version);
        if (IsAppImage)
        {
            if (!File.Exists(payload)) throw new IOException("AppImage preparado não encontrado.");
        }
        else UpdatePayload.ValidateDirectory(payload, "PhantomMonitor");
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in new[] { script, Environment.ProcessId.ToString(), payload,
            IsAppImage ? _appImagePath! : AppContext.BaseDirectory, "PhantomMonitor", IsAppImage ? "appimage" : "portable" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o atualizador.");
    }
}
