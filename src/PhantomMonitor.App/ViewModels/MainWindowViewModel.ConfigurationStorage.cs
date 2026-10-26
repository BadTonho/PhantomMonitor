using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _configurationWarning = "";
    private bool _configurationLoadFailed;
    public string ConfigurationWarning { get => _configurationWarning; private set { if (Set(ref _configurationWarning, value)) Raise(nameof(HasConfigurationWarning)); } }
    public bool HasConfigurationWarning => ConfigurationWarning.Length > 0;

    private void InitializeConfigurationWarning(string? loadError)
    {
        if (!ConfigPaths.IsPortable) return;
        _configurationLoadFailed = loadError is not null;
        if (_configurationLoadFailed)
            ConfigurationWarning = $"{loadError} Confira as permissões da pasta portátil e reinicie o aplicativo. Caminho: {ConfigPaths.ConfigFile}";
        else if (!ConfigPaths.CanWritePortableConfig(ConfigPaths.ConfigFile))
            ConfigurationWarning = $"Não é possível gravar a configuração na pasta portátil. Confira as permissões: {ConfigPaths.ConfigFile}";
    }

    private bool SaveConfiguration()
    {
        if (_configurationLoadFailed) return false;
        if (!_config.Save(ConfigPaths.ConfigFile))
        {
            if (ConfigPaths.IsPortable)
                ConfigurationWarning = $"Não foi possível salvar a configuração portátil. Confira as permissões: {ConfigPaths.ConfigFile}";
            return false;
        }
        if (ConfigPaths.IsPortable) ConfigurationWarning = "";
        return true;
    }
}
