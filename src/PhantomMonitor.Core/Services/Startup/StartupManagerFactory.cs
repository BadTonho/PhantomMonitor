using PhantomMonitor.Services.Linux;

namespace PhantomMonitor.Services;

/// <summary>Startup por SO: registro Run no Windows, entrada XDG autostart no Linux (null = sem suporte).</summary>
public static class StartupManagerFactory
{
    public static IStartupManager? Create()
    {
        if (OperatingSystem.IsWindows()) return WindowsStartupManager.Shared;
        if (OperatingSystem.IsLinux()) return LinuxStartupManager.Shared;
        return null;
    }
}
