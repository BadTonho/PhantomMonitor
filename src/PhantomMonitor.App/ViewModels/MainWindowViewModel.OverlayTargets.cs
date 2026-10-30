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
    public bool IsOverlayTargetLaunchVisible => IsOverlayInGameModeSelected && (IsLinuxPlatform || IsWindowsPlatform);
    public bool IsOverlayInGameModeSelected => OverlayRenderModeIndex == 1;
    public string SteamOverlayLaunchOption
    {
        get
        {
            var openGlLibrary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "lib", "libPhantomOpenGLOverlay.so");
            if (!File.Exists(openGlLibrary)) return "ENABLE_PHANTOM_OVERLAY=1 %command%";
            var escapedPath = openGlLibrary.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("$", "\\$", StringComparison.Ordinal)
                .Replace("`", "\\`", StringComparison.Ordinal);
            return $"ENABLE_PHANTOM_OVERLAY=1 LD_PRELOAD=\"{escapedPath}${{LD_PRELOAD:+:$LD_PRELOAD}}\" %command%";
        }
    }
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
        if (target is null || (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())) return;
        if (!File.Exists(target.ExecutablePath))
        {
            OverlayTargetMessage = "O executável não foi encontrado no caminho salvo.";
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var result = await Services.Overlay.WindowsOverlayHookService.LaunchAsync(target.ExecutablePath, target.Arguments);
                OverlayTargetMessage = result.Message;
                return;
            }
            var startInfo = new ProcessStartInfo
            {
                FileName = target.ExecutablePath,
                Arguments = target.Arguments,
                WorkingDirectory = Path.GetDirectoryName(target.ExecutablePath) ?? Environment.CurrentDirectory,
                UseShellExecute = false
            };
            startInfo.Environment["ENABLE_PHANTOM_OVERLAY"] = "1";
            startInfo.Environment.Remove("DISABLE_PHANTOM_OVERLAY");
            var openGlLibrary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "lib", "libPhantomOpenGLOverlay.so");
            if (File.Exists(openGlLibrary))
            {
                var existingPreload = startInfo.Environment.TryGetValue("LD_PRELOAD", out var value) ? value : null;
                startInfo.Environment["LD_PRELOAD"] = string.IsNullOrWhiteSpace(existingPreload)
                    ? openGlLibrary
                    : openGlLibrary + ":" + existingPreload;
            }
            Process.Start(startInfo)?.Dispose();
            OverlayTargetMessage = File.Exists(openGlLibrary)
                ? $"Iniciado: {Path.GetFileName(target.ExecutablePath)} com Vulkan e preload GLX/EGL."
                : $"Iniciado: {Path.GetFileName(target.ExecutablePath)}. Vulkan pode funcionar; instale a biblioteca OpenGL para jogos GLX/EGL.";
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
