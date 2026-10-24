using System.Collections.ObjectModel;
using System.Diagnostics;
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
    public bool IsLinuxPlatform => OperatingSystem.IsLinux();
    public bool IsWindowsPlatform => OperatingSystem.IsWindows();
    public bool IsOverlayInGameModeSelected => OverlayRenderModeIndex == 1;
    public string SteamOverlayLaunchOption => "ENABLE_PHANTOM_OVERLAY=1 %command%";
    public void SetOverlayTargetMessage(string message) => OverlayTargetMessage = message;

    private void LoadOverlayInGameTargets()
    {
        OverlayInGameTargets.Clear();
        foreach (var target in _config.Overlay.InGameTargets)
            OverlayInGameTargets.Add(CloneOverlayTarget(target));
    }

    public void AddOverlayInGameTarget()
    {
        var path = OverlayTargetPathText.Trim().Trim('"');
        if (path.Length == 0 || !File.Exists(path))
        {
            OverlayTargetMessage = "Selecione um executável existente.";
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
        OverlayTargetPathText = "";
        OverlayTargetArgumentsText = "";
        OverlayTargetMessage = "Executável adicionado. Use Salvar e aplicar para gravar a lista.";
    }

    public void RemoveOverlayInGameTarget(OverlayLaunchTargetConfig? target)
    {
        if (target is not null && OverlayInGameTargets.Remove(target))
            OverlayTargetMessage = "Executável removido. Use Salvar e aplicar para gravar a lista.";
    }

    public void LaunchOverlayTarget(OverlayLaunchTargetConfig? target)
    {
        if (!OperatingSystem.IsLinux() || target is null) return;
        if (!File.Exists(target.ExecutablePath))
        {
            OverlayTargetMessage = "O executável não foi encontrado no caminho salvo.";
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target.ExecutablePath,
                Arguments = target.Arguments,
                WorkingDirectory = Path.GetDirectoryName(target.ExecutablePath) ?? Environment.CurrentDirectory,
                UseShellExecute = false
            };
            startInfo.Environment["ENABLE_PHANTOM_OVERLAY"] = "1";
            startInfo.Environment.Remove("DISABLE_PHANTOM_OVERLAY");
            Process.Start(startInfo);
            OverlayTargetMessage = $"Iniciado: {Path.GetFileName(target.ExecutablePath)}.";
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
}
