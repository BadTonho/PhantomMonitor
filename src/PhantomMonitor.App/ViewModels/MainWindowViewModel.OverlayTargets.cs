using System.Collections.ObjectModel;
using PhantomMonitor.App.Services.Overlay;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _overlayTargetPathText = "";
    private string _overlayTargetArgumentsText = "";
    private string _overlayTargetMessage = "";

    public ObservableCollection<OverlayLaunchTargetConfig> OverlayInGameTargets { get; } = [];
    public string OverlayTargetPathText { get => _overlayTargetPathText; set => Set(ref _overlayTargetPathText, value); }
    public string OverlayTargetArgumentsText { get => _overlayTargetArgumentsText; set => Set(ref _overlayTargetArgumentsText, value); }
    public string OverlayTargetMessage { get => _overlayTargetMessage; private set => Set(ref _overlayTargetMessage, value); }
    public bool IsWindowsPlatform => OperatingSystem.IsWindows();
    public bool IsLinuxPlatform => OperatingSystem.IsLinux();
    public bool IsOverlayTargetLaunchVisible => IsOverlayInGameModeSelected && IsWindowsPlatform;
    public bool IsOverlayInGameModeSelected => OverlayRenderModeIndex == 1;
    public string SteamOverlayLaunchOption => "phantom-hud %command%";
    public string LauncherWrapperOption => "phantom-hud";
    public string LinuxHudLocationText => LinuxOverlayNativeFiles.InstalledHudExecutable;
    public bool IsPhantomHudAvailable => OperatingSystem.IsLinux() && LinuxOverlayNativeFiles.HasInstalledHudExecutable;
    public void SetOverlayTargetMessage(string message) => OverlayTargetMessage = message;

    private void LoadOverlayInGameTargets()
    {
        OverlayInGameTargets.Clear();
        foreach (var target in _config.Overlay.InGameTargets)
            OverlayInGameTargets.Add(CloneOverlayTarget(target));
    }

    public void AddOverlayInGameTarget()
    {
        var requestedPath = OverlayTargetPathText.Trim().Trim('"');
        var path = ResolveOverlayTargetPath(requestedPath);
        if (path is null)
        {
            OverlayTargetMessage = "Informe o caminho de um executável existente ou um comando disponível no PATH.";
            return;
        }

        if (OverlayInGameTargets.Any(target => string.Equals(target.ExecutablePath, path,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
        {
            OverlayTargetMessage = "Esse executável já está na lista.";
            return;
        }

        if (OverlayInGameTargets.Count >= 64)
        {
            OverlayTargetMessage = "A lista aceita até 64 executáveis.";
            return;
        }

        OverlayInGameTargets.Add(new OverlayLaunchTargetConfig
        {
            ExecutablePath = path,
            Arguments = OverlayTargetArgumentsText.Trim()
        });
        OnSettingsDraftChanged();
        OverlayTargetPathText = "";
        OverlayTargetArgumentsText = "";
        OverlayTargetMessage = "Executável adicionado. Use Salvar e aplicar para gravar a lista.";
    }

    public void RemoveOverlayInGameTarget(OverlayLaunchTargetConfig? target)
    {
        if (target is not null && OverlayInGameTargets.Remove(target))
        {
            OnSettingsDraftChanged();
            OverlayTargetMessage = "Executável removido. Use Salvar e aplicar para gravar a lista.";
        }
    }

    public async Task LaunchOverlayTargetAsync(OverlayLaunchTargetConfig? target)
    {
        if (target is null || !OperatingSystem.IsWindows()) return;
        if (!File.Exists(target.ExecutablePath))
        {
            OverlayTargetMessage = "O executável não foi encontrado no caminho salvo.";
            return;
        }

        try
        {
            var result = await Services.Overlay.WindowsOverlayHookService.LaunchAsync(target.ExecutablePath, target.Arguments);
            OverlayTargetMessage = result.Message;
        }
        catch (Exception ex)
        {
            OverlayTargetMessage = $"Não foi possível iniciar o executável: {ex.Message}";
            ErrorLog.Write("OverlayTarget.Launch", ex);
        }
    }
    internal bool OverlayInGameTargetsChanged(IReadOnlyList<OverlayLaunchTargetConfig> saved)
    {
        if (saved.Count != OverlayInGameTargets.Count) return true;
        for (var index = 0; index < saved.Count; index++)
        {
            if (!string.Equals(saved[index].ExecutablePath, OverlayInGameTargets[index].ExecutablePath,
                    StringComparison.Ordinal)
                || !string.Equals(saved[index].Arguments, OverlayInGameTargets[index].Arguments,
                    StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    internal static OverlayLaunchTargetConfig CloneOverlayTarget(OverlayLaunchTargetConfig target)
        => new() { ExecutablePath = target.ExecutablePath, Arguments = target.Arguments };

    private static string? ResolveOverlayTargetPath(string requestedPath)
    {
        if (requestedPath.Length == 0) return null;
        if (File.Exists(requestedPath)) return Path.GetFullPath(requestedPath);
        return null;
    }
}
