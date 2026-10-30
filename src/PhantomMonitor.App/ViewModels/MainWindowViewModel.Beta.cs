namespace PhantomMonitor.App.ViewModels;

/// <summary>Preferências beta de visibilidade dos itens da navegação.</summary>
public sealed partial class MainWindowViewModel
{
    private bool _showCustomSensorsInSidebar;
    private bool _showSensorTuningInSidebar;
    private bool _showAlertsInSidebar;

    public bool ShowCustomSensorsInSidebar
    {
        get => _showCustomSensorsInSidebar;
        set => SaveBetaSidebarPreference(ref _showCustomSensorsInSidebar, value,
            nameof(ShowCustomSensorsInSidebar), "Sensores customizados",
            enabled => _config.Beta.ShowCustomSensorsInSidebar = enabled);
    }

    public bool ShowSensorTuningInSidebar
    {
        get => _showSensorTuningInSidebar;
        set => SaveBetaSidebarPreference(ref _showSensorTuningInSidebar, value,
            nameof(ShowSensorTuningInSidebar), "Ajuste por série",
            enabled => _config.Beta.ShowSensorTuningInSidebar = enabled);
    }

    public bool ShowAlertsInSidebar
    {
        get => _showAlertsInSidebar;
        set => SaveBetaSidebarPreference(ref _showAlertsInSidebar, value,
            nameof(ShowAlertsInSidebar), "Alertas",
            enabled => _config.Beta.ShowAlertsInSidebar = enabled);
    }

    private void SaveBetaSidebarPreference(ref bool field, bool value, string propertyName,
        string itemName, Action<bool> updateConfig)
    {
        if (field == value) return;

        var previous = field;
        field = value;
        Raise(propertyName);

        updateConfig(value);

        if (!SaveConfiguration())
        {
            field = previous;
            updateConfig(previous);
            Raise(propertyName);
            SettingsMessage = "Não foi possível salvar a preferência Beta no config.json. A barra lateral foi restaurada.";
            return;
        }

        SettingsMessage = value
            ? $"{itemName} ativado na barra lateral."
            : $"{itemName} ocultado da barra lateral.";
    }
}
