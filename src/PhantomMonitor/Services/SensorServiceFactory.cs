namespace PhantomMonitor.Services;

/// <summary>
/// Fábrica multiplataforma que inicializa o provedor de sensores correto
/// conforme o sistema operacional em execução (Windows ou Linux).
/// </summary>
public static class SensorServiceFactory
{
    public static ISensorService Create(int updateIntervalMs = 1000, AppConfig? config = null)
    {
        // Sensores customizados (fórmulas do usuário): engine única compartilhada
        // com o provedor escolhido; configuração vazia custa zero por ciclo.
        var customSensors = CustomSensorEngine.FromConfig(config);

        if (OperatingSystem.IsWindows())
        {
            return new HardwareMonitorService(updateIntervalMs, customSensors);
        }

        // Linux e demais sistemas POSIX (macOS usa o mesmo caminho /proc best-effort).
        return new Linux.LinuxSensorService(updateIntervalMs, customSensors);
    }
}
