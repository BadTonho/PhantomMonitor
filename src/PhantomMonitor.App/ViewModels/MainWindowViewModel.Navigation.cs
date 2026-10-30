namespace PhantomMonitor.App.ViewModels;

/// <summary>Preferência imediata para a posição da navegação principal.</summary>
public sealed partial class MainWindowViewModel
{
    private string _navigationPosition = "left";
    private string _navigationPositionMessage = "";

    public IReadOnlyList<string> NavigationPositionOptions { get; } = ["Lateral (esquerda)", "Inferior"];
    public string NavigationPosition { get => _navigationPosition; private set => Set(ref _navigationPosition, value); }
    public int NavigationPositionIndex
    {
        get => NavigationPosition == "bottom" ? 1 : 0;
        set => SetNavigationPosition(value == 1 ? "bottom" : "left");
    }
    public bool IsBottomNavigation => NavigationPosition == "bottom";
    public string NavigationPositionMessage
    {
        get => _navigationPositionMessage;
        private set
        {
            if (Set(ref _navigationPositionMessage, value)) Raise(nameof(HasNavigationPositionMessage));
        }
    }
    public bool HasNavigationPositionMessage => NavigationPositionMessage.Length > 0;

    private void SetNavigationPosition(string value)
    {
        if (NavigationPosition == value) return;

        var previous = NavigationPosition;
        _config.NavigationPosition = value;
        NavigationPosition = value;
        Raise(nameof(NavigationPositionIndex));
        Raise(nameof(IsBottomNavigation));
        NavigationPositionMessage = "";

        if (!SaveConfiguration())
        {
            _config.NavigationPosition = previous;
            NavigationPosition = previous;
            Raise(nameof(NavigationPositionIndex));
            Raise(nameof(IsBottomNavigation));
            NavigationPositionMessage = "Não foi possível salvar a posição da navegação. O layout anterior foi restaurado.";
            SettingsMessage = NavigationPositionMessage;
            return;
        }

        NavigationPositionMessage = value == "bottom"
            ? "Navegação inferior aplicada e salva."
            : "Navegação lateral aplicada e salva.";
        SettingsMessage = NavigationPositionMessage;
    }
}
