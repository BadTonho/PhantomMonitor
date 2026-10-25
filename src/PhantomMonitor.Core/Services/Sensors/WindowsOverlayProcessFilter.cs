namespace PhantomMonitor.Services;

internal static class WindowsOverlayProcessFilter
{
    private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PhantomMonitor", "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "SearchIndexer", "TextInputHost", "LockApp", "dwm", "ApplicationFrameHost", "devenv",
        "RuntimeBroker", "Widgets", "WidgetService", "SystemSettings", "backgroundTaskHost", "sihost",
        "taskhostw", "ctfmon", "conhost", "csrss", "services", "svchost", "winlogon", "wininit",
        "lsass", "smss", "spoolsv", "fontdrvhost", "audiodg", "WUDFHost", "LogonUI", "System",
        "Registry", "Idle", "Memory Compression", "SecurityHealthService", "SecurityHealthSystray"
    };

    internal static bool IsIgnored(string processName)
    {
        var normalized = Path.GetFileNameWithoutExtension(processName.Trim().Trim('"'));
        return IgnoredNames.Contains(normalized);
    }
}
