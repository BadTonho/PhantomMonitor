using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PhantomMonitor.Services.Update;

/// <summary>
/// Applier do Windows: portable zip (também é o payload do instalado por
/// Inno Setup em %LOCALAPPDATA%\Programs — a pasta é a mesma coisa). Baixa
/// <c>PhantomMonitor-&lt;v&gt;-win-x64.zip</c>, descompacta em
/// <c>%LOCALAPPDATA%\PhantomMonitor\UpdateStaging\&lt;v&gt;\</c> e escreve o
/// <c>apply_update.cmd</c> one-shot: espera o PID encerrar (instância única
/// garantida pelo mutex Local\PhantomMonitor.SingleInstance), renomeia os
/// arquivos trocados para "*.bak", copia o staging, reabre o app e limpa os
/// backups; em falha de cópia, restaura o ".bak" de imediato. Com o publish
/// self-contained single-file, a troca é de ~3 arquivos (exe + PresentMon.exe
/// + config.example.json do usuário segue em %APPDATA%, intocado).
/// </summary>
public sealed class WindowsUpdateApplier : IUpdateApplier
{
    public static WindowsUpdateApplier Shared { get; } = new();

    public string AssetNameForVersion(string version) => $"PhantomMonitor-{version}-win-x64.zip";

    /// <summary>
    /// A prova de que dá para aplicar é gravar na própria pasta de instalação
    /// (portable extraído em lugar protegido, como Program Files, não pode).
    /// </summary>
    public bool CanApply
    {
        get
        {
            try
            {
                var probe = Path.Combine(InstallDirectory, ".phantom-update-probe");
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

    private static string InstallDirectory => AppContext.BaseDirectory;

    public async Task<string> PrepareAsync(GitHubReleaseAsset asset, string version, Action<double>? progress, CancellationToken cancellation)
    {
        var stagingRoot = UserDataPaths.UpdateStagingDirectory;
        var versionDir = Path.Combine(stagingRoot, version);
        Directory.CreateDirectory(versionDir);
        CleanupOldStaging(stagingRoot, keepVersion: version);

        // Download direto para a raiz do staging; apagado após extrair.
        var zipPath = Path.Combine(stagingRoot, $"PhantomMonitor-{version}-win-x64.zip");
        await GitHubReleaseSource.DownloadFileAsync(asset, zipPath, progress, cancellation).ConfigureAwait(false);

        await ExtractZipAsync(zipPath, versionDir, progress, cancellation).ConfigureAwait(false);
        File.Delete(zipPath);
        File.WriteAllText(
            Path.Combine(stagingRoot, "apply_update.cmd"),
            BuildApplyScript(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return version;
    }

    public void Apply(string version)
    {
        var stagingRoot = UserDataPaths.UpdateStagingDirectory;
        var stagingDir = Path.Combine(stagingRoot, version);
        var scriptPath = Path.Combine(stagingRoot, "apply_update.cmd");

        if (!Directory.Exists(stagingDir) || !File.Exists(scriptPath))
            throw new InvalidOperationException("Staging da atualização não existe mais; refaça a preparação.");

        // É substituído pelo updater em %LOCALAPPDATA%\Programs\<pasta>:
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "PhantomMonitor.exe");
        if (string.IsNullOrWhiteSpace(exeName)) exeName = "PhantomMonitor.exe";

        // Formato clássico do cmd: /c + aspas duplas externas para o script com
        // espaços no caminho + um argumento entre aspas por campo.
        var arguments = $"/c \"\"{scriptPath}\" {Environment.ProcessId} \"{stagingDir}\" \"{InstallDirectory}\" \"{exeName}\"\"";
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static async Task ExtractZipAsync(string zipPath, string versionDir, Action<double>? progress, CancellationToken cancellation)
    {
        var rootWithSeparator = Path.GetFullPath(versionDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);

        var total = archive.Entries.Sum(entry => entry.Length);
        long done = 0;

        foreach (var entry in archive.Entries)
        {
            var targetPath = ResolveEntryPath(rootWithSeparator, versionDir, entry.FullName);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath); // apenas pasta
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await using var source = entry.Open();
            await using var destination = File.Create(targetPath);
            await source.CopyToAsync(destination, cancellation).ConfigureAwait(false);
            done += entry.Length;
            progress?.Invoke(total > 0 ? Math.Clamp(done * 100.0 / total, 0, 100) : 100);
        }
    }

    /// <summary>Proteção de zip-slip: o nome vem da release; nada pode sair do staging.</summary>
    private static string ResolveEntryPath(string rootWithSeparator, string versionDir, string entryName)
    {
        var targetPath = Path.GetFullPath(Path.Combine(versionDir, entryName));
        if (!targetPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetPath.TrimEnd(Path.DirectorySeparatorChar), rootWithSeparator.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Conteúdo inesperado no pacote: '{entryName}'.");
        return targetPath;
    }

    private static void CleanupOldStaging(string stagingRoot, string keepVersion)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(stagingRoot))
                if (!string.Equals(Path.GetFileName(directory), keepVersion, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(directory, recursive: true);

            foreach (var file in Directory.EnumerateFiles(stagingRoot, "*.zip"))
                File.Delete(file);
        }
        catch (Exception ex)
        {
            // Staging antigo preso não pode travar a nova atualização.
            ErrorLog.Write("WindowsUpdateApplier.Cleanup", ex);
        }
    }

    /// <summary>
    /// One-shot em CMD puro (nada extra de runtime). ASCII sem acentos, por
    /// conta do console do Windows, e com espera por PID seguida de troca com
    /// backup/rollback por arquivo.
    /// </summary>
    private static string BuildApplyScript() => string.Join("\r\n",
    [
        "@echo off",
        "rem PhantomMonitor: troca one-shot da atualizacao.",
        "rem Uso: apply_update.cmd <pid> <stagingDir> <targetDir> <exeName>",
        "setlocal",
        "set \"PID=%~1\"",
        "set \"STAGING=%~2\"",
        "set \"TARGET=%~3\"",
        "set \"EXE=%~4\"",
        "set /A ELAPSED=0",
        "",
        ":WAIT_EXIT",
        "if \"%PID%\"==\"0\" goto SWAP",
        "tasklist /FI \"PID eq %PID%\" 2>nul | find /I \"%PID%\" >nul 2>&1",
        "if errorlevel 1 goto SWAP",
        "set /A ELAPSED+=500",
        "if %ELAPSED% GEQ 30000 goto SWAP",
        "ping -n 1 127.0.0.1 >nul 2>&1",
        "goto WAIT_EXIT",
        "",
        ":SWAP",
        "rem Limpa backups sobreviventes de uma execucao anterior do updater.",
        "del /Q \"%TARGET%\\*.bak\" >nul 2>&1",
        "set /A FAIL=0",
        "for %%F in (\"%STAGING%\\*\") do call :SWAP_FILE \"%%~fF\"",
        "if not \"%FAIL%\"==\"0\" goto FAILED",
        "",
        "rem Sucesso: apaga os backups desta rodada e reabre o app.",
        "del /Q \"%TARGET%\\*.bak\" >nul 2>&1",
        "start \"PhantomMonitor\" /D \"%TARGET%\" \"%TARGET%\\%EXE%\"",
        "goto DONE",
        "",
        ":FAILED",
        "echo Falha na troca dos arquivos; backups preservados em %TARGET%.",
        "exit /b 1",
        "",
        ":DONE",
        "endlocal",
        "exit /b 0",
        "",
        ":SWAP_FILE",
        "if not \"%FAIL%\"==\"0\" exit /b",
        "set \"SWAPSRC=%~1\"",
        "set \"SWAPNAME=%~nx1\"",
        "set \"SWAPDST=%TARGET%\\%SWAPNAME%\"",
        "if not exist \"%SWAPDST%\" goto COPY_NEW",
        "ren \"%SWAPDST%\" \"%SWAPNAME%.bak\" >nul 2>&1",
        "if errorlevel 1 (",
        "    set FAIL=1",
        "    exit /b",
        ")",
        "",
        ":COPY_NEW",
        "copy /Y \"%SWAPSRC%\" \"%SWAPDST%\" >nul 2>&1",
        "if errorlevel 1 (",
        "    set FAIL=1",
        "    if exist \"%SWAPDST%.bak\" ren \"%SWAPDST%.bak\" \"%SWAPNAME%\" >nul 2>&1",
        ")",
        "exit /b",
        "",
    ]);
}
