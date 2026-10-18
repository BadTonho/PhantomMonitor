namespace RivaMonitor.Services;

/// <summary>
/// Fábrica multiplataforma que inicializa o provedor de sensores correto
/// conforme o sistema operacional em execução (Windows ou Linux).
/// </summary>
public static class SensorServiceFactory
{
    public static ISensorService Create(int updateIntervalMs = 1000)
    {
        if (OperatingSystem.IsWindows())
        {
            return new HardwareMonitorService(updateIntervalMs);
        }

        if (OperatingSystem.IsLinux())
        {
            return new Linux.LinuxSensorService(updateIntervalMs);
        }

        // Fallback genérico caso executado em outro ambiente POSIX
        return new Linux.LinuxSensorService(updateIntervalMs);
    }
}
