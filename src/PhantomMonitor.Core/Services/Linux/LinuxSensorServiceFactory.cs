namespace PhantomMonitor.Services.Linux;

/// <summary>Cria o provedor Linux com as opções portáveis do config atual.</summary>
public static class LinuxSensorServiceFactory
{
    public static ISensorService Create(AppConfig? config = null)
    {
        config ??= new AppConfig();
        return new LinuxSensorService(
            config.UpdateIntervalMs,
            CustomSensorEngine.FromConfig(config),
            SensorTuningEngine.FromConfig(config));
    }
}
