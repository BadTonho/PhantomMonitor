using System.IO;

namespace PhantomMonitor.Services;

/// <summary>Paths for persistent data owned by the current user.</summary>
public static class UserDataPaths
{
    public static string LogDirectory
    {
        get
        {
            var dataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(dataRoot))
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                dataRoot = string.IsNullOrWhiteSpace(userProfile)
                    ? AppContext.BaseDirectory
                    : Path.Combine(userProfile, ".local", "share");
            }

            return Path.Combine(dataRoot, "PhantomMonitor", "logs");
        }
    }
}
