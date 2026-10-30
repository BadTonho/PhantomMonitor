namespace PhantomMonitor.Services;

/// <summary>Preferências das opções beta expostas pela interface.</summary>
public sealed class BetaConfig
{
    /// <summary>Mostra o atalho de sensores customizados na barra lateral.</summary>
    public bool ShowCustomSensorsInSidebar { get; set; }

    /// <summary>Mostra o atalho de ajuste por série na barra lateral.</summary>
    public bool ShowSensorTuningInSidebar { get; set; }

    /// <summary>Mostra o atalho de alertas na barra lateral.</summary>
    public bool ShowAlertsInSidebar { get; set; }
}
