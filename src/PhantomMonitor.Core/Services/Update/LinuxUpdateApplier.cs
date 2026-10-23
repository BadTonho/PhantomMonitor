using System.Diagnostics;
using System.IO;
using System.Text;

namespace PhantomMonitor.Services.Update;

/// <summary>
/// Applier do Linux em dois formatos:
/// - **Portable (tarball)**: baixa <c>PhantomMonitor-&lt;v&gt;-linux-x64.tar.gz</c>,
///   extrai no staging (<c>$XDG_DATA_HOME/PhantomMonitor/UpdateStaging/&lt;v&gt;</c>,
///   via <c>tar</c> do sistema) e o one-shot <c>apply_update.sh</c> troca os
///   arquivos quando o processo encerra (kill -0), reabrindo o executável.
/// - **AppImage** (<c>$APPIMAGE</c> definido): baixa o AppImage novo; o script
///   apenas substitui o arquivo, dá chmod +x e o reabre.
/// Instalação por pacote do sistema (pasta não gravável, ex.: .deb) não suporta
/// a troca: <see cref="IUpdateApplier.CanApply"/> é false e o app fica no modo
/// notificação com link para a página de releases.
/// </summary>
public sealed class LinuxUpdateApplier : IUpdateApplier
{
    public static LinuxUpdateApplier Shared { get; } = new();

    private readonly bool _isAppImage;
    private readonly string? _appImagePath;

    public LinuxUpdateApplier()
    {
        _appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        _isAppImage = !string.IsNullOrWhiteSpace(_appImagePath) && File.Exists(_appImagePath);
    }

    public string AssetNameForVersion(string version) => _isAppImage
        ? $"PhantomMonitor-{version}-linux-x64.AppImage"
        : $"PhantomMonitor-{version}-linux-x64.tar.gz";

    /// <summary>
    /// Portable: prova gravando na pasta de instalação (instalação de sistema,
    /// como .deb em /usr, falha e cai para notificar). AppImage: prova a pasta
    /// do arquivo — abrir o próprio binário em execução para escrita daria
    /// ETXTBSY, então o probe concentra na pasta que o cerca.
    /// </summary>
    public bool CanApply => _isAppImage
        ? CanWriteDirectory(Path.GetDirectoryName(_appImagePath) ?? "/tmp")
        : CanWriteDirectory(InstallDirectory);

    private static string InstallDirectory => AppContext.BaseDirectory;

    public async Task<string> PrepareAsync(GitHubReleaseAsset asset, string version, Action<double>? progress, CancellationToken cancellation)
    {
        var stagingRoot = UserDataPaths.UpdateStagingDirectory;
        var versionDir = Path.Combine(stagingRoot, version);
        Directory.CreateDirectory(versionDir);
        CleanupOldStaging(stagingRoot, version);

        var assetPath = Path.Combine(stagingRoot, AssetNameForVersion(version));
        await GitHubReleaseSource.DownloadFileAsync(asset, assetPath, progress, cancellation).ConfigureAwait(false);

        if (_isAppImage)
        {
            // O staging guarda o AppImage novo; o script copia para $APPIMAGE.
        }
        else
        {
            // tar existe em toda distribuição Linux; não reimplementamos o
            // formato tar à mão no Core.
            var extraction = new ProcessStartInfo("/bin/sh", $"-c \"tar -xzf '{assetPath}' -C '{versionDir}'\"")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var tar = Process.Start(extraction) ?? throw new IOException("Não foi possível executar o tar do sistema.");
            var error = await tar.StandardError.ReadToEndAsync(cancellation).ConfigureAwait(false);
            await tar.WaitForExitAsync(cancellation).ConfigureAwait(false);
            if (tar.ExitCode != 0)
                throw new IOException(string.IsNullOrWhiteSpace(error)
                    ? $"Falha ao extrair o tarball (tar saiu com {tar.ExitCode})."
                    : $"Falha ao extrair o tarball: {error.Trim()}.");
            File.Delete(assetPath);
        }

        File.WriteAllText(Path.Combine(stagingRoot, "apply_update.sh"), BuildScript(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return version;
    }

    public void Apply(string version)
    {
        var stagingRoot = UserDataPaths.UpdateStagingDirectory;
        var scriptPath = Path.Combine(stagingRoot, "apply_update.sh");
        if (!File.Exists(scriptPath))
            throw new InvalidOperationException("Staging da atualização não existe mais; refaça a preparação.");

        // Formato do script: <pid|0> <payload> <target> <exe|-> <portable|appimage>.
        var arguments = $"\"{scriptPath}\" {Environment.ProcessId} ";
        if (_isAppImage)
        {
            var payload = Path.Combine(stagingRoot, version, AssetNameForVersion(version));
            if (!File.Exists(payload))
                throw new InvalidOperationException("AppImage do staging não existe mais; refaça a preparação.");
            arguments += $"\"{payload}\" \"{_appImagePath}\" - appimage";
        }
        else
        {
            var stagingDir = Path.Combine(stagingRoot, version);
            if (!Directory.Exists(stagingDir))
                throw new InvalidOperationException("Arquivos do staging não existem mais; refaça a preparação.");
            var exeName = Path.GetFileName(Environment.ProcessPath ?? "PhantomMonitor");
            arguments += $"\"{stagingDir}\" \"{InstallDirectory}\" \"{(string.IsNullOrWhiteSpace(exeName) ? "PhantomMonitor" : exeName)}\" portable";
        }

        // Script roda com /bin/sh (POSIX); não depende de bash nem de pacotes extras.
        using var process = Process.Start(new ProcessStartInfo("/bin/sh", arguments) { UseShellExecute = false });
    }

    private static void CleanupOldStaging(string stagingRoot, string keepVersion)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(stagingRoot))
                if (!string.Equals(Path.GetFileName(directory), keepVersion, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(directory, recursive: true);

            foreach (var pattern in new[] { "*.tar.gz", "*.zip", "*.AppImage" })
                foreach (var file in Directory.EnumerateFiles(stagingRoot, pattern))
                    File.Delete(file);
        }
        catch (Exception ex)
        {
            // Staging antigo preso não pode travar a nova atualização.
            ErrorLog.Write("LinuxUpdateApplier.Cleanup", ex);
        }
    }

    /// <summary>
    /// One-shot em POSIX sh (sem bash). Espera o PID sair (até 60 s), troca com
    /// backup/rollback por arquivo (portable) ou substitui o AppImage, e reabre
    /// com nohup para sobreviver ao fim do próprio script.
    /// </summary>
    private static string BuildScript() => string.Join("\n",
    [
        "#!/bin/sh",
        "# PhantomMonitor: troca one-shot da atualizacao (portable ou AppImage).",
        "# Uso: apply_update.sh <pid|0> <payload> <target> <exe|-> <portable|appimage>",
        "PID=\"$1\"; PAYLOAD=\"$2\"; TARGET=\"$3\"; EXE=\"$4\"; MODE=\"$5\"",
        "",
        "i=0",
        "while [ \"$i\" -lt 60 ]; do",
        "  [ \"$PID\" = \"0\" ] && break",
        "  kill -0 \"$PID\" 2>/dev/null || break",
        "  sleep 1",
        "  i=$((i + 1))",
        "done",
        "",
        "if [ \"$MODE\" = \"appimage\" ]; then",
        "  cp -f \"$PAYLOAD\" \"$TARGET\" || exit 1",
        "  chmod +x \"$TARGET\" || exit 1",
        "  cd \"$(dirname \"$TARGET\")\" || exit 1",
        "  nohup \"$TARGET\" >/dev/null 2>&1 &",
        "  exit 0",
        "fi",
        "",
        "rm -f \"$TARGET\"/*.bak 2>/dev/null",
        "fail=0",
        "for src in \"$PAYLOAD\"/*; do",
        "  [ -e \"$src\" ] || continue",
        "  name=\"${src##*/}\"",
        "  dst=\"$TARGET/$name\"",
        "  if [ -e \"$dst\" ] || [ -L \"$dst\" ]; then",
        "    mv \"$dst\" \"$dst.bak\" || { fail=1; break; }",
        "  fi",
        "  cp -f \"$src\" \"$dst\" || { fail=1; [ -e \"$dst.bak\" ] && mv \"$dst.bak\" \"$dst\" 2>/dev/null; break; }",
        "done",
        "if [ \"$fail\" -ne 0 ]; then",
        "  echo \"Falha na troca dos arquivos; backups preservados em $TARGET.\"",
        "  exit 1",
        "fi",
        "",
        "rm -f \"$TARGET\"/*.bak 2>/dev/null",
        "cd \"$TARGET\" || exit 1",
        "nohup \"./$EXE\" >/dev/null 2>&1 &",
        "exit 0",
        "",
    ]);

    private static bool CanWriteDirectory(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, ".phantom-update-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
