using System.Formats.Tar;
using System.IO.Compression;

if (args.Length != 2) throw new ArgumentException("Uso: ReleasePackager <publish-linux> <arquivo.tar.gz>");
var source = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (!File.Exists(Path.Combine(source, "PhantomMonitor"))) throw new IOException("Executável Linux ausente.");
if (output.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new IOException("O arquivo de saída precisa ficar fora do payload.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using var destination = File.Create(output);
using var gzip = new GZipStream(destination, CompressionLevel.Optimal);
using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
const UnixFileMode readMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Links não permitidos no payload.");
    var name = Path.GetRelativePath(source, directory).Replace('\\', '/');
    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./" + name + "/") { Mode = readMode | execute });
}
foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
    .OrderBy(path => Path.GetFileName(path) == "PhantomMonitor" ? 0 : Path.GetFileName(path) == "PhantomMonitor.App" ? 2 : 1)
    .ThenBy(path => path, StringComparer.Ordinal))
{
    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Links não permitidos no payload.");
    var name = Path.GetRelativePath(source, file).Replace('\\', '/');
    if (name == "PhantomMonitor.App")
    {
        writer.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "./PhantomMonitor.App")
        { LinkName = "./PhantomMonitor", Mode = readMode | execute });
        continue;
    }
    using var input = File.OpenRead(file);
    var executable = name == "PhantomMonitor" || name.EndsWith(".sh", StringComparison.Ordinal);
    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./" + name)
    { DataStream = input, Mode = executable ? readMode | execute : readMode });
}
Console.WriteLine($"Tar Linux gerado com permissões POSIX explícitas: {Path.GetFileName(output)}");
