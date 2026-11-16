namespace PhantomMonitor.Services.Update;

/// <summary>
/// Limpeza pós-atualização. Cada troca deixa o payload baixado (staging) e uma cópia completa dos arquivos
/// antigos (backup); sem limpeza isso soma de 100 a 200 MB por versão. A regra é conservadora: nunca apaga
/// o que ainda pode ser necessário (versão pronta para aplicar, backup mais recente ou transação incompleta).
/// </summary>
public static class UpdateHousekeeping
{
    private const string BackupPrefix = ".phantom-update-backup";

    /// <summary>Roda a limpeza padrão para esta instalação; nunca lança (falhas vão ao ErrorLog).</summary>
    public static void Run()
    {
        if (UpdateVersion.IsDevelopmentBuild) return;
        try
        {
            RemoveInstalledStaging(UserDataPaths.UpdateStagingDirectory, UpdateVersion.CurrentParsed);
            var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            var installDirectory = !string.IsNullOrWhiteSpace(appImage) && Path.IsPathFullyQualified(appImage)
                ? Path.GetDirectoryName(appImage)
                : AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(installDirectory)) PruneOldBackups(installDirectory);
        }
        catch (Exception ex) { ErrorLog.WriteThrottled("UpdateHousekeeping.Run", ex); }
    }

    /// <summary>Apaga os diretórios de staging de versões que já estão instaladas (≤ versão atual).</summary>
    internal static void RemoveInstalledStaging(string stagingRoot, SemanticVersion? current)
    {
        if (current is not { } running || !Directory.Exists(stagingRoot)) return;
        foreach (var directory in Directory.EnumerateDirectories(stagingRoot))
        {
            if (!UpdateVersion.TryParse(Path.GetFileName(directory), out var staged)) continue;
            if (staged.CompareTo(running) > 0) continue; // ainda pode ser aplicada
            try { Directory.Delete(directory, recursive: true); }
            catch (Exception ex) { ErrorLog.WriteThrottled("UpdateHousekeeping.Staging", ex); }
        }
    }

    /// <summary>
    /// Mantém só o backup mais recente. Backups de transações que não terminaram em "committed"
    /// (rolled-back/recovery-required) nunca são apagados: podem ser a única cópia para recuperação manual.
    /// </summary>
    internal static void PruneOldBackups(string installDirectory)
    {
        if (!Directory.Exists(installDirectory)) return;
        var entries = new DirectoryInfo(installDirectory)
            .EnumerateFileSystemInfos(BackupPrefix + "*")
            .OrderByDescending(entry => entry.CreationTimeUtc)
            .ToArray();
        foreach (var entry in entries.Skip(1))
        {
            try
            {
                if (entry is DirectoryInfo directory)
                {
                    var status = Path.Combine(directory.FullName, "status");
                    if (!File.Exists(status) || !File.ReadAllText(status).Trim().Equals("committed", StringComparison.Ordinal))
                        continue;
                    directory.Delete(recursive: true);
                }
                else entry.Delete(); // AppImage: o backup é uma cópia do arquivo anterior
            }
            catch (Exception ex) { ErrorLog.WriteThrottled("UpdateHousekeeping.Backup", ex); }
        }
    }
}
