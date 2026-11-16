using PhantomMonitor.Services.Update;

public sealed class UpdateHousekeepingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("phantom-housekeeping-").FullName;
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Staging_de_versoes_instaladas_some_e_a_pendente_fica()
    {
        foreach (var version in new[] { "0.1.0", "0.1.1", "0.2.0" })
        {
            Directory.CreateDirectory(Path.Combine(_root, version));
            File.WriteAllText(Path.Combine(_root, version, "x"), "x");
        }
        File.WriteAllText(Path.Combine(_root, "apply_update.sh"), "#");
        Assert.True(UpdateVersion.TryParse("0.1.1", out var current));

        UpdateHousekeeping.RemoveInstalledStaging(_root, current);

        Assert.False(Directory.Exists(Path.Combine(_root, "0.1.0")));
        Assert.False(Directory.Exists(Path.Combine(_root, "0.1.1")));
        Assert.True(Directory.Exists(Path.Combine(_root, "0.2.0"))); // ainda aplicável
        Assert.True(File.Exists(Path.Combine(_root, "apply_update.sh")));
    }

    [Fact]
    public void Mantem_so_o_backup_mais_recente_e_preserva_transacoes_incompletas()
    {
        string Backup(string name, string status, int minutesAgo)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            if (status.Length > 0) File.WriteAllText(Path.Combine(path, "status"), status);
            Directory.SetCreationTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
            return path;
        }
        var recente = Backup(".phantom-update-backup.new", "committed", 1);
        var antigoOk = Backup(".phantom-update-backup.old", "committed", 60);
        var falhou = Backup(".phantom-update-backup.fail", "recovery-required: x", 120);
        var semStatus = Backup(".phantom-update-backup.nostatus", "", 180);
        var appImageAntigo = Path.Combine(_root, ".phantom-update-backup.img");
        File.WriteAllText(appImageAntigo, "img");
        File.SetCreationTimeUtc(appImageAntigo, DateTime.UtcNow.AddMinutes(-240));

        UpdateHousekeeping.PruneOldBackups(_root);

        Assert.True(Directory.Exists(recente));
        Assert.False(Directory.Exists(antigoOk));
        Assert.True(Directory.Exists(falhou));
        Assert.True(Directory.Exists(semStatus));
        Assert.False(File.Exists(appImageAntigo));
    }
}
