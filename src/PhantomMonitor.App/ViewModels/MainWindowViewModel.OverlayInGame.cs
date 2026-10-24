using Avalonia.Threading;
using System.ComponentModel;
using System.Diagnostics;
using PhantomMonitor.App.Services.Overlay;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly InGameOverlayFramePublisher _inGameOverlayFramePublisher = new();
    private string _inGameOverlayDiagnosticText = "Canal nativo ainda não foi aberto.";
    private bool _inGameFrameChannelActive;
    private DispatcherTimer? _inGameOverlayHeartbeat;
    private readonly Dictionary<int, DateTimeOffset> _windowsOverlayInjectionAttempts = [];
    private readonly HashSet<int> _windowsOverlayInjectedProcessIds = [];
    private readonly HashSet<int> _windowsOverlayLiveProcessIds = [];
    private bool _windowsOverlayProcessScanActive;
    private string _windowsOverlayInjectionDiagnostic = "";

    public string InGameOverlayDiagnosticText
    {
        get => _inGameOverlayDiagnosticText;
        private set => Set(ref _inGameOverlayDiagnosticText, value);
    }

    public bool IsInGameOverlayDiagnosticsVisible => OverlayRenderMode == "inGame";
    public string InGameOverlayCapabilityNotice => OperatingSystem.IsLinux()
        ? "Linux: Vulkan usa a camada ativada por jogo; OpenGL usa preload GLX/EGL. O modo não abre janelas flutuantes automaticamente."
        : OperatingSystem.IsWindows()
            ? "Windows: hooks x86/x64 para Direct3D 9–12 e OpenGL em executáveis cadastrados; Vulkan é ativado ao iniciar pelo PhantomMonitor. O app também detecta jogos autorizados iniciados por outros launchers enquanto estiver aberto. Anti-cheats podem recusar o hook; não há troca automática para janelas flutuantes."
            : "O renderer dentro do jogo não está disponível nesta plataforma. O modo selecionado não alterna automaticamente para janelas flutuantes.";

    private void PublishInGameOverlayFrame(bool initializeOrClear = false, bool forceHidden = false)
    {
        if (_disposed) return;
        if (OverlayRenderMode != "inGame" && !_inGameFrameChannelActive && !initializeOrClear) return;
        try
        {
            _inGameOverlayFramePublisher.Publish(InGameOverlayFrameSerializer.Build(this,
                forceHidden || OverlayRenderMode != "inGame"));
            _inGameFrameChannelActive = OverlayRenderMode == "inGame";
            InGameOverlayDiagnosticText = $"{GetInGameOverlayPlatformStatus()}\n{_inGameOverlayFramePublisher.Status}\n{GetNativeOverlayStatusText()}";
        }
        catch (Exception ex)
        {
            InGameOverlayDiagnosticText = $"Falha ao montar quadro nativo: {ex.Message}";
            ErrorLog.Write("InGameOverlay.Publish", ex);
        }
    }

    private void ConfigureInGameOverlayHeartbeat()
    {
        if (OverlayRenderMode == "inGame")
        {
            _inGameOverlayHeartbeat ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _inGameOverlayHeartbeat.Tick -= OnInGameOverlayHeartbeat;
            _inGameOverlayHeartbeat.Tick += OnInGameOverlayHeartbeat;
            if (!_inGameOverlayHeartbeat.IsEnabled)
                _inGameOverlayHeartbeat.Start();
            PublishInGameOverlayFrame();
            return;
        }

        _inGameOverlayHeartbeat?.Stop();
        PublishInGameOverlayFrame(initializeOrClear: true, forceHidden: true); // limpa estado deixado por outra execução.
    }

    private async void OnInGameOverlayHeartbeat(object? sender, EventArgs e)
    {
        if (_disposed) return;
        PublishInGameOverlayFrame();
        if (!OperatingSystem.IsWindows() || OverlayRenderMode != "inGame" || _windowsOverlayProcessScanActive)
            return;

        _windowsOverlayProcessScanActive = true;
        try
        {
            var candidates = await Task.Run(FindAllowedWindowsProcesses);
            if (_disposed) return;
            var livePids = candidates.Select(candidate => candidate.ProcessId).ToHashSet();
            _windowsOverlayLiveProcessIds.Clear();
            _windowsOverlayLiveProcessIds.UnionWith(livePids);
            _windowsOverlayInjectedProcessIds.RemoveWhere(pid => !livePids.Contains(pid));
            foreach (var pid in _windowsOverlayInjectionAttempts.Keys.Where(pid => !livePids.Contains(pid)).ToArray())
                _windowsOverlayInjectionAttempts.Remove(pid);

            var alreadyPublished = InGameOverlayNativeStatusReader.ReadWindows(livePids)
                .Select(status => status.ProcessId).ToHashSet();
            _windowsOverlayInjectedProcessIds.UnionWith(alreadyPublished);
            foreach (var candidate in candidates)
            {
                if (_windowsOverlayInjectedProcessIds.Contains(candidate.ProcessId)) continue;
                if (_windowsOverlayInjectionAttempts.TryGetValue(candidate.ProcessId, out var attemptedAt)
                    && DateTimeOffset.UtcNow - attemptedAt < TimeSpan.FromSeconds(20)) continue;

                _windowsOverlayInjectionAttempts[candidate.ProcessId] = DateTimeOffset.UtcNow;
                var result = await WindowsOverlayHookService.InjectAsync(candidate.ProcessId, candidate.ExecutablePath);
                if (_disposed) return;
                _windowsOverlayInjectionDiagnostic = result.Success
                    ? $"PID {candidate.ProcessId}: DLL carregada; aguardando o renderer DXGI."
                    : $"PID {candidate.ProcessId}: hook não instalado: {result.Message}";
                if (result.Success) _windowsOverlayInjectedProcessIds.Add(candidate.ProcessId);
            }
        }
        catch (Exception ex)
        {
            _windowsOverlayInjectionDiagnostic = $"Falha ao observar jogos autorizados: {ex.Message}";
            ErrorLog.Write("InGameOverlay.ProcessMonitor", ex);
        }
        finally
        {
            _windowsOverlayProcessScanActive = false;
        }
    }

    private string GetInGameOverlayPlatformStatus()
    {
        if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var manifestCandidates = new[]
            {
                Path.Combine(home, ".local", "share", "vulkan", "implicit_layer.d", "phantom_vulkan_layer.json"),
                "/etc/vulkan/implicit_layer.d/phantom_vulkan_layer.json",
                "/usr/share/vulkan/implicit_layer.d/phantom_vulkan_layer.json"
            };
            var libraryCandidates = new[]
            {
                Path.Combine(home, ".local", "lib", "libVkLayer_phantom_monitor.so"),
                "/usr/lib/libVkLayer_phantom_monitor.so",
                "/usr/lib64/libVkLayer_phantom_monitor.so",
                "/usr/local/lib/libVkLayer_phantom_monitor.so"
            };
            var openGlLibrary = Path.Combine(home, ".local", "lib", "libPhantomOpenGLOverlay.so");
            return $"Plataforma: Linux · camada Vulkan {(manifestCandidates.Any(File.Exists) && libraryCandidates.Any(File.Exists) ? "instalada" : "não encontrada")} · preload OpenGL {(File.Exists(openGlLibrary) ? "instalado" : "não encontrado")}.";
        }

        if (OperatingSystem.IsWindows())
            return $"Plataforma: Windows · executáveis autorizados: {OverlayInGameTargets.Count}; hooks Direct3D/OpenGL x86/x64 e camada Vulkan no início pelo app.";

        return "Plataforma: sem integração nativa para overlay dentro do jogo.";
    }

    private string GetNativeOverlayStatusText()
    {
        IReadOnlyList<InGameOverlayNativeStatus> statuses;
        try
        {
            statuses = OperatingSystem.IsLinux()
                ? InGameOverlayNativeStatusReader.ReadLinux()
                : OperatingSystem.IsWindows()
                    ? InGameOverlayNativeStatusReader.ReadWindows(_windowsOverlayLiveProcessIds)
                    : [];
        }
        catch (Exception ex)
        {
            return $"Falha ao consultar diagnóstico nativo: {ex.Message}";
        }

        var nativeText = statuses.Count == 0
            ? OperatingSystem.IsLinux()
                ? "API detectada: nenhum processo Vulkan publicou estado. Inicie o jogo com ENABLE_PHANTOM_OVERLAY=1."
                : "API detectada: nenhum processo autorizado publicou estado do hook."
            : string.Join("\n", statuses.Select(status =>
                $"PID {status.ProcessId} · {status.ApiName} · {status.StateName}: {status.Message}"));
        return _windowsOverlayInjectionDiagnostic.Length == 0
            ? nativeText
            : nativeText + "\n" + _windowsOverlayInjectionDiagnostic;
    }

    private IReadOnlyList<(int ProcessId, string ExecutablePath)> FindAllowedWindowsProcesses()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var allowedPaths = OverlayInGameTargets
            .Select(target => target.ExecutablePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowedPaths.Count == 0) return [];

        var targetNames = allowedPaths.Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var processes = new Dictionary<int, string>();
        foreach (var name in targetNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (!WindowsOverlayHookService.TryGetExecutablePath(process.Id, out var executablePath))
                            continue;
                        var executable = Path.GetFullPath(executablePath);
                        if (allowedPaths.Contains(executable)) processes[process.Id] = executable;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or
                                               NotSupportedException or ArgumentException) { }
                }
            }
        }
        return processes.Select(item => (item.Key, item.Value)).ToArray();
    }
}
