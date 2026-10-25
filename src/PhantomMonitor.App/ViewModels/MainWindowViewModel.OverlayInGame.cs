using Avalonia.Threading;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
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
    private readonly HashSet<int> _windowsOverlayInjectionAttemptedProcessIds = [];
    private readonly HashSet<int> _windowsOverlayInjectedProcessIds = [];
    private readonly HashSet<int> _windowsOverlayLiveProcessIds = [];
    private readonly Dictionary<int, long> _windowsOverlayProcessStartTimes = [];
    private WindowsVulkanImplicitLayerRegistration? _windowsVulkanLayerRegistration;
    private bool _windowsOverlayProcessScanActive;
    private string _windowsOverlayInjectionDiagnostic = "";
    private string _windowsVulkanLayerDiagnostic = "";

    [SupportedOSPlatform("windows")]
    private WindowsVulkanImplicitLayerRegistration WindowsVulkanLayerRegistration =>
        _windowsVulkanLayerRegistration ??= new WindowsVulkanImplicitLayerRegistration();

    public string InGameOverlayDiagnosticText
    {
        get => _inGameOverlayDiagnosticText;
        private set => Set(ref _inGameOverlayDiagnosticText, value);
    }

    public bool IsInGameOverlayDiagnosticsVisible => OverlayRenderMode == "inGame";
    public string InGameOverlayCapabilityNotice => OperatingSystem.IsLinux()
        ? "Linux: Vulkan usa a camada ativada por jogo; OpenGL usa preload GLX/EGL. O modo não abre janelas flutuantes automaticamente."
        : OperatingSystem.IsWindows()
            ? "Windows: enquanto este modo estiver ativo, o PresentMon detecta aplicações apresentando frames e o PhantomMonitor tenta instalar hooks x86/x64 para Direct3D 9–12 e OpenGL, sem cadastro de executável. Vulkan usa uma camada implícita por usuário. ETW pode exigir a conta no grupo Performance Log Users; processos elevados ou protegidos podem recusar acesso. Anti-cheats podem bloquear o hook; não há troca automática para janelas flutuantes."
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
        if (OperatingSystem.IsWindows())
        {
            var shouldEnable = OverlayRenderMode == "inGame";
            try
            {
                if (_sensorService is IOverlayProcessDiscovery discovery)
                {
                    discovery.OverlayProcessDiscoveryEnabled = shouldEnable;
                    if (!shouldEnable)
                        _windowsOverlayLiveProcessIds.Clear();
                }
                else
                {
                    _windowsOverlayInjectionDiagnostic = "Detecção global indisponível: o serviço não fornece a sessão PresentMon.";
                }

                _windowsVulkanLayerDiagnostic = shouldEnable
                    ? WindowsVulkanLayerRegistration.Enable(AppContext.BaseDirectory)
                    : WindowsVulkanLayerRegistration.Disable(AppContext.BaseDirectory);
            }
            catch (Exception ex)
            {
                _windowsVulkanLayerDiagnostic = $"Falha ao configurar a camada Vulkan: {ex.Message}";
                ErrorLog.Write("InGameOverlay.VulkanRegistration", ex);
            }
        }

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
            var candidates = await Task.Run(FindPresentingWindowsProcesses);
            if (_disposed || OverlayRenderMode != "inGame") return;
            var livePids = candidates.Select(candidate => candidate.ProcessId).ToHashSet();
            _windowsOverlayLiveProcessIds.Clear();
            _windowsOverlayLiveProcessIds.UnionWith(livePids);

            foreach (var item in _windowsOverlayProcessStartTimes.ToArray())
            {
                if (IsSameLiveWindowsProcess(item.Key, item.Value)) continue;
                _windowsOverlayProcessStartTimes.Remove(item.Key);
                _windowsOverlayInjectedProcessIds.Remove(item.Key);
                _windowsOverlayInjectionAttempts.Remove(item.Key);
                _windowsOverlayInjectionAttemptedProcessIds.Remove(item.Key);
            }

            var alreadyPublished = InGameOverlayNativeStatusReader.ReadWindows(livePids)
                .Select(status => status.ProcessId).ToHashSet();
            _windowsOverlayInjectedProcessIds.UnionWith(alreadyPublished);
            foreach (var candidate in candidates)
            {
                if (_disposed || OverlayRenderMode != "inGame") return;
                if (candidate.StartTimeUtcTicks > 0)
                {
                    if (_windowsOverlayProcessStartTimes.TryGetValue(candidate.ProcessId, out var knownStart)
                        && knownStart != candidate.StartTimeUtcTicks)
                    {
                        _windowsOverlayInjectedProcessIds.Remove(candidate.ProcessId);
                        _windowsOverlayInjectionAttempts.Remove(candidate.ProcessId);
                        _windowsOverlayInjectionAttemptedProcessIds.Remove(candidate.ProcessId);
                    }
                    _windowsOverlayProcessStartTimes[candidate.ProcessId] = candidate.StartTimeUtcTicks;
                }
                if (candidate.ResolutionError is not null)
                {
                    if (_windowsOverlayInjectionAttempts.TryGetValue(candidate.ProcessId, out var attemptedAt)
                        && DateTimeOffset.UtcNow - attemptedAt < TimeSpan.FromSeconds(20)) continue;
                    _windowsOverlayInjectionAttempts[candidate.ProcessId] = DateTimeOffset.UtcNow;
                    _windowsOverlayInjectionDiagnostic = $"PID {candidate.ProcessId}: não foi possível validar o executável: {candidate.ResolutionError}.";
                    continue;
                }

                _windowsOverlayInjectionAttempts.Remove(candidate.ProcessId);
                if (_windowsOverlayInjectedProcessIds.Contains(candidate.ProcessId)
                    || !_windowsOverlayInjectionAttemptedProcessIds.Add(candidate.ProcessId)) continue;
                var result = await WindowsOverlayHookService.InjectAsync(candidate.ProcessId, candidate.ExecutablePath);
                if (_disposed || OverlayRenderMode != "inGame") return;
                _windowsOverlayInjectionDiagnostic = result.Success
                    ? $"PID {candidate.ProcessId}: hook instalado; aguardando a primeira apresentação compatível."
                    : $"PID {candidate.ProcessId}: hook não instalado: {result.Message}";
                if (result.Success) _windowsOverlayInjectedProcessIds.Add(candidate.ProcessId);
            }
        }
        catch (Exception ex)
        {
            _windowsOverlayInjectionDiagnostic = $"Falha ao observar aplicações apresentando frames: {ex.Message}";
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
            return $"Plataforma: Windows · detecção global por ETW/PresentMon ativa no modo Dentro do jogo; hooks x86/x64 para Direct3D/OpenGL e Vulkan implícito por usuário. Jogos Vulkan abertos antes da ativação precisam ser reiniciados. {_windowsVulkanLayerDiagnostic}";

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
                : "API detectada: nenhum processo detectado apresentando frames publicou estado do hook."
            : string.Join("\n", statuses.Select(status =>
                $"PID {status.ProcessId} · {status.ApiName} · {status.StateName}: {status.Message}"));
        var etwDiagnostic = OperatingSystem.IsWindows()
            ? (_sensorService as IOverlayProcessDiscovery)?.OverlayDiscoveryDiagnostic
            : null;
        return string.Join("\n", new[] { nativeText,
            string.IsNullOrWhiteSpace(etwDiagnostic) ? null : $"ETW: {etwDiagnostic}",
            _windowsOverlayInjectionDiagnostic.Length == 0 ? null : _windowsOverlayInjectionDiagnostic }
            .Where(value => value is not null));
    }

    private IReadOnlyList<WindowsOverlayCandidate> FindPresentingWindowsProcesses()
    {
        if (!OperatingSystem.IsWindows()) return [];
        if (_sensorService is not IOverlayProcessDiscovery discovery) return [];
        var processes = new Dictionary<int, WindowsOverlayCandidate>();
        foreach (var presenting in discovery.GetRecentPresentingProcesses())
        {
            if (presenting.ProcessId == Environment.ProcessId ||
                WindowsOverlayProcessFilter.IsIgnored(presenting.Application)) continue;
            try
            {
                using var process = Process.GetProcessById(presenting.ProcessId);
                if (WindowsOverlayProcessFilter.IsIgnored(process.ProcessName)) continue;
                var startTimeTicks = process.StartTime.ToUniversalTime().Ticks;
                if (!WindowsOverlayHookService.TryGetExecutablePath(process.Id, out var executablePath, out var error))
                {
                    processes[process.Id] = new WindowsOverlayCandidate(process.Id, "", startTimeTicks,
                        $"{error}; não será tentada injeção sem validar a imagem do processo");
                    continue;
                }

                processes[process.Id] = new WindowsOverlayCandidate(process.Id,
                    Path.GetFullPath(executablePath), startTimeTicks, null);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or
                                       NotSupportedException or ArgumentException or UnauthorizedAccessException)
            {
                processes[presenting.ProcessId] = new WindowsOverlayCandidate(presenting.ProcessId, "", 0,
                    ex is Win32Exception { NativeErrorCode: 5 }
                        ? "acesso negado ao consultar a identidade do processo"
                        : ex.Message);
            }
        }
        return processes.Values.OrderBy(candidate => candidate.ProcessId).ToArray();
    }

    private static bool IsSameLiveWindowsProcess(int processId, long startTimeUtcTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime().Ticks == startTimeUtcTicks;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or
                                   NotSupportedException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record WindowsOverlayCandidate(int ProcessId, string ExecutablePath,
        long StartTimeUtcTicks, string? ResolutionError);

    private void CleanupWindowsOverlayState()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_sensorService is IOverlayProcessDiscovery discovery)
            discovery.OverlayProcessDiscoveryEnabled = false;
        var result = WindowsVulkanLayerRegistration.Disable(AppContext.BaseDirectory);
        if (!result.StartsWith("Camada Vulkan removida", StringComparison.Ordinal))
            ErrorLog.Write("InGameOverlay.VulkanRegistration", result);
    }
}
