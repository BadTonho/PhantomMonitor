namespace PhantomMonitor.App.ViewModels;

/// <summary>Preferências imediatas da navegação principal.</summary>
public sealed partial class MainWindowViewModel
{
    private string _navigationPosition = "left";
    private string _navigationDisplayMode = "iconsAndText";
    private string _navigationSettingsMessage = "";

    public IReadOnlyList<string> NavigationPositionOptions { get; } =
        ["Lateral (esquerda)", "Inferior", "Dock flutuante"];
    public IReadOnlyList<string> NavigationDisplayModeOptions { get; } =
        ["Ícones e textos", "Somente ícones"];

    public string NavigationPosition { get => _navigationPosition; private set => Set(ref _navigationPosition, value); }
    public int NavigationPositionIndex
    {
        get => NavigationPosition switch { "bottom" => 1, "dock" => 2, _ => 0 };
        set => SetNavigationPosition(value switch { 1 => "bottom", 2 => "dock", _ => "left" });
    }

    public string NavigationDisplayMode { get => _navigationDisplayMode; private set => Set(ref _navigationDisplayMode, value); }
    public int NavigationDisplayModeIndex
    {
        get => NavigationDisplayMode == "iconsOnly" ? 1 : 0;
        set => SetNavigationDisplayMode(value == 1 ? "iconsOnly" : "iconsAndText");
    }

    public bool IsLeftNavigation => NavigationPosition == "left";
    public bool IsBottomNavigation => NavigationPosition == "bottom";
    public bool IsDockNavigation => NavigationPosition == "dock";
    public bool ShowNavigationLabels => NavigationDisplayMode != "iconsOnly";
    public string DockStatusTooltip => UpdateBadgeVisible
        ? $"{StatusText}\n{UpdateBadgeText}\nClique para abrir Configurações."
        : StatusText;

    public string NavigationSettingsMessage
    {
        get => _navigationSettingsMessage;
        private set
        {
            if (Set(ref _navigationSettingsMessage, value))
                Raise(nameof(HasNavigationSettingsMessage));
        }
    }
    public bool HasNavigationSettingsMessage => NavigationSettingsMessage.Length > 0;

    private void SetNavigationPosition(string value)
    {
        if (NavigationPosition == value) return;

        var previous = NavigationPosition;
        _config.NavigationPosition = value;
        NavigationPosition = value;
        RaiseNavigationLayoutProperties();
        NavigationSettingsMessage = "";

        if (!SaveConfiguration())
        {
            _config.NavigationPosition = previous;
            NavigationPosition = previous;
            RaiseNavigationLayoutProperties();
            SetNavigationSaveError();
            return;
        }

        NavigationSettingsMessage = value switch
        {
            "bottom" => "Navegação inferior aplicada e salva.",
            "dock" => "Dock flutuante aplicado e salvo.",
            _ => "Navegação lateral aplicada e salva."
        };
        SettingsMessage = NavigationSettingsMessage;
    }

    private void SetNavigationDisplayMode(string value)
    {
        if (NavigationDisplayMode == value) return;

        var previous = NavigationDisplayMode;
        _config.NavigationDisplayMode = value;
        NavigationDisplayMode = value;
        Raise(nameof(NavigationDisplayModeIndex));
        Raise(nameof(ShowNavigationLabels));
        NavigationSettingsMessage = "";

        if (!SaveConfiguration())
        {
            _config.NavigationDisplayMode = previous;
            NavigationDisplayMode = previous;
            Raise(nameof(NavigationDisplayModeIndex));
            Raise(nameof(ShowNavigationLabels));
            SetNavigationSaveError();
            return;
        }

        NavigationSettingsMessage = value == "iconsOnly"
            ? "Navegação somente com ícones aplicada e salva."
            : "Navegação com ícones e textos aplicada e salva.";
        SettingsMessage = NavigationSettingsMessage;
    }

    private void RaiseNavigationLayoutProperties()
    {
        Raise(nameof(NavigationPositionIndex));
        Raise(nameof(IsLeftNavigation));
        Raise(nameof(IsBottomNavigation));
        Raise(nameof(IsDockNavigation));
    }

    private void SetNavigationSaveError()
    {
        NavigationSettingsMessage = "Não foi possível salvar as preferências da navegação. O layout anterior foi restaurado.";
        SettingsMessage = NavigationSettingsMessage;
    }

    private void OnNavigationStatusChanged() => Raise(nameof(DockStatusTooltip));
}
