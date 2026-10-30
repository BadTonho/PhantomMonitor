namespace PhantomMonitor.App.ViewModels;

/// <summary>Preferências beta de visibilidade dos itens da navegação.</summary>
public sealed partial class MainWindowViewModel
{
    private bool _showCustomSensorsInSidebar = true;
    private bool _showSensorTuningInSidebar = true;

    public bool ShowCustomSensorsInSidebar
    {
        get => _showCustomSensorsInSidebar;
        set => SaveBetaSidebarPreference(ref _showCustomSensorsInSidebar, value,
            nameof(ShowCustomSensorsInSidebar), customSensors: true);
    }

    public bool ShowSensorTuningInSidebar
    {
        get => _showSensorTuningInSidebar;
        set => SaveBetaSidebarPreference(ref _showSensorTuningInSidebar, value,
            nameof(ShowSensorTuningInSidebar), customSensors: false);
    }

    private void SaveBetaSidebarPreference(ref bool field, bool value, string propertyName, bool customSensors)
    {
        if (field == value) return;

        var previous = field;
        field = value;
        Raise(propertyName);

        if (customSensors)
            _config.Beta.ShowCustomSensorsInSidebar = value;
        else
            _config.Beta.ShowSensorTuningInSidebar = value;

        if (!SaveConfiguration())
        {
            field = previous;
            if (customSensors)
                _config.Beta.ShowCustomSensorsInSidebar = previous;
            else
                _config.Beta.ShowSensorTuningInSidebar = previous;
            Raise(propertyName);
            SettingsMessage = "Não foi possível salvar a preferência Beta no config.json. A barra lateral foi restaurada.";
            return;
        }

        var name = customSensors ? "Sensores customizados" : "Ajuste por série";
        SettingsMessage = value
            ? $"{name} ativado na barra lateral."
            : $"{name} ocultado da barra lateral.";
    }
}
