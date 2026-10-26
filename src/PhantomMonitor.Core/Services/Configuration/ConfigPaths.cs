using Microsoft.Win32;
using System.Runtime.Versioning;

namespace PhantomMonitor.Services;

public enum ConfigurationLocationKind { Development, Installed, Portable }
public sealed record ConfigurationLocation(ConfigurationLocationKind Kind, string ConfigFile);

/// <summary>Determines where this particular distribution keeps its configuration.</summary>
public static class ConfigPaths
{
    public const string PortableMarker = "phantom.portable";
    public const string InstalledMarker = "phantom.installed";
    private static readonly Lazy<ConfigurationLocation> CurrentLocation = new(ResolveCurrent);
    public static string ConfigFile => CurrentLocation.Value.ConfigFile;
    public static bool IsPortable => CurrentLocation.Value.Kind == ConfigurationLocationKind.Portable;

    private static ConfigurationLocation ResolveCurrent()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomMonitor");
        var windows = OperatingSystem.IsWindows();
        var legacyInstall = windows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PhantomMonitor")
            : "/usr/lib/phantom-monitor";
        var registered = windows && IsRegisteredWindowsInstallation(AppContext.BaseDirectory);
        return ResolveFor(AppContext.BaseDirectory, appData, Environment.GetEnvironmentVariable("APPIMAGE"),
            windows, legacyInstall, registered);
    }

    internal static ConfigurationLocation ResolveFor(string executableDirectory, string userConfigDirectory,
        string? appImagePath, bool windows, string? legacyInstallDirectory, bool registeredInstallation)
    {
        var baseDirectory = Path.GetFullPath(executableDirectory);
        if (!windows && !string.IsNullOrWhiteSpace(appImagePath) && Path.IsPathFullyQualified(appImagePath))
            return new(ConfigurationLocationKind.Portable, Path.Combine(Path.GetDirectoryName(appImagePath)!, "config.json"));

        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var installed = File.Exists(Path.Combine(baseDirectory, InstalledMarker)) || registeredInstallation
            || !string.IsNullOrWhiteSpace(legacyInstallDirectory)
               && Path.TrimEndingDirectorySeparator(baseDirectory).Equals(
                   Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyInstallDirectory)), comparison);
        if (installed) return new(ConfigurationLocationKind.Installed, Path.Combine(userConfigDirectory, "config.json"));
        if (File.Exists(Path.Combine(baseDirectory, PortableMarker)))
            return new(ConfigurationLocationKind.Portable, Path.Combine(baseDirectory, "config.json"));

        for (var dir = new DirectoryInfo(baseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PhantomMonitor.slnx"))
                || File.Exists(Path.Combine(dir.FullName, "PhantomMonitor.sln")))
                return new(ConfigurationLocationKind.Development, Path.Combine(dir.FullName, "config.json"));
        return new(ConfigurationLocationKind.Installed, Path.Combine(userConfigDirectory, "config.json"));
    }

    [SupportedOSPlatform("windows")]
    private static bool IsRegisteredWindowsInstallation(string executableDirectory)
    {
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8D5E4F61-A1B4-4F0E-8E7A-3D9C2F0B7A11}_is1";
        try
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey(keyPath);
                if (key is null) continue;
                var location = key.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(location) && SameDirectory(location, executableDirectory)) return true;
                var icon = (key.GetValue("DisplayIcon") as string)?.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(icon))
                {
                    var iconPath = icon.Split(',')[0].Trim().Trim('"');
                    var parent = Path.GetDirectoryName(iconPath);
                    if (parent is not null && SameDirectory(parent, executableDirectory)) return true;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException) { }
        return false;
    }

    private static bool SameDirectory(string left, string right) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    internal static bool CanWritePortableConfig(string path, Func<string, Stream>? createProbe = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return false;
        var probe = Path.Combine(directory, $".phantom-config-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var file = createProbe is null
                ? new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)
                : createProbe(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
}
