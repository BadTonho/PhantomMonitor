namespace PhantomMonitor.Services.Update;

internal static class UpdatePayload
{
    internal static string VersionDirectory(string root, string version)
    {
        if (!UpdateVersion.TryParse(version, out _) || version != Path.GetFileName(version)
            || version.Contains('/') || version.Contains('\\'))
            throw new ArgumentException("Versão de atualização inválida.", nameof(version));
        return Path.Combine(root, version);
    }

    internal static string ReadScript(string name)
    {
        using var stream = typeof(UpdatePayload).Assembly.GetManifestResourceStream($"PhantomMonitor.UpdateScripts.{name}")
            ?? throw new InvalidOperationException("Script de atualização não encontrado.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static void ValidateDirectory(string directory, string executable)
    {
        if (!File.Exists(Path.Combine(directory, executable)))
            throw new IOException($"O pacote não contém {executable}.");
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.TryPop(out var parent))
            foreach (var entry in Directory.EnumerateFileSystemEntries(parent))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("O pacote de atualização contém links não permitidos.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
    }

    internal static bool CanWriteDirectory(string directory)
    {
        var probe = Path.Combine(directory, $".phantom-update-probe-{Guid.NewGuid():N}");
        try { using (File.Create(probe)) { } File.Delete(probe); return true; }
        catch { return false; }
    }
}
