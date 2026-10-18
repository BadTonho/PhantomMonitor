using System.IO;

namespace RivaMonitor.Services;

/// <summary>
/// Resolve o caminho do config.json de forma independente do diretório de saída.
/// - Em desenvolvimento (repositório detectado subindo a partir do executável): config.json na raiz do repo.
/// - Caso contrário (publish/instalado): %AppData%\RivaMonitor\config.json.
/// </summary>
public static class ConfigPaths
{
    private static string? _cached;

    public static string ConfigFile => _cached ??= Resolve();

    private static string Resolve()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RivaMonitor.slnx")) ||
                File.Exists(Path.Combine(dir.FullName, "RivaMonitor.sln")))
            {
                return Path.Combine(dir.FullName, "config.json");
            }
            dir = dir.Parent;
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RivaMonitor");
        return Path.Combine(appData, "config.json");
    }
}
