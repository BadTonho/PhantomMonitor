using System.IO;

namespace PhantomMonitor.Services;

/// <summary>Paths for persistent data owned by the current user.</summary>
public static class UserDataPaths
{
    /// <summary>
    /// Raiz de dados do usuário: %LOCALAPPDATA% no Windows e ~/.local/share
    /// (fallback) quando a pasta especial não existe (Linux via.Mon, etc).
    /// </summary>
    private static string ResolveDataRoot()
    {
        var dataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dataRoot = string.IsNullOrWhiteSpace(userProfile)
                ? AppContext.BaseDirectory
                : Path.Combine(userProfile, ".local", "share");
        }

        return dataRoot;
    }

    public static string LogDirectory =>
        Path.Combine(ResolveDataRoot(), "PhantomMonitor", "logs");

    /// <summary>
    /// Raiz do staging de atualização: a versão nova baixada fica em um
    /// subdiretório "<c>1.2.3</c>" e um updater one-shot troca os arquivos
    /// quando o app encerra (ver IUpdateApplier).
    /// </summary>
    public static string UpdateStagingDirectory =>
        Path.Combine(ResolveDataRoot(), "PhantomMonitor", "UpdateStaging");
}
