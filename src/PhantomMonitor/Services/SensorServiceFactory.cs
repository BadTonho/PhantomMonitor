namespace PhantomMonitor.Services;

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

        // Linux e demais sistemas POSIX (macOS usa o mesmo caminho /proc best-effort).
        return new Linux.LinuxSensorService(updateIntervalMs);
    }
}
