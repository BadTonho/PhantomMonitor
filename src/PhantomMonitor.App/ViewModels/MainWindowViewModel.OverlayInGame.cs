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
    private string _linuxOverlayRegistrationDiagnostic = "";
    private string? _linuxConfiguredSessionMode;
    private readonly SemaphoreSlim _linuxOverlaySessionConfiguration = new(1, 1);
    private Task _linuxOverlaySessionConfigurationTask = Task.CompletedTask;

    [SupportedOSPlatform("windows")]
    private WindowsVulkanImplicitLayerRegistration WindowsVulkanLayerRegistration =>
        _windowsVulkanLayerRegistration ??= new WindowsVulkanImplicitLayerRegistration();

    public string InGameOverlayDiagnosticText
    {
        get => _inGameOverlayDiagnosticText;
        private set => Set(ref _inGameOverlayDiagnosticText, value);
    }

    public bool IsInGameOverlayDiagnosticsVisible => OverlayRenderMode == "inGame";
    public bool IsLinuxOverlaySessionWarningVisible => OperatingSystem.IsLinux()
        && OverlayRenderMode != "inGame"
        && !string.IsNullOrWhiteSpace(_linuxOverlayRegistrationDiagnostic);
    public string LinuxOverlaySessionDiagnosticText => _linuxOverlayRegistrationDiagnostic;
    public string InGameOverlayCapabilityNotice => OperatingSystem.IsLinux()
        ? "Linux: o PhantomMonitor prepara captura passiva de FPS via Vulkan e GLX/EGL, sem desenhar dentro do jogo no modo flutuante. O modo Dentro do jogo também ativa o renderer. A captura exige iniciar o jogo depois de o ambiente ser atualizado; launchers e jogos já abertos precisam ser reabertos. Flatpak pode exigir integração própria."
        : OperatingSystem.IsWindows()
            ? "Windows: enquanto este modo estiver ativo, o PresentMon detecta aplicações apresentando frames e o PhantomMonitor tenta instalar hooks x86/x64 para Direct3D 9–12 e OpenGL, sem cadastro de executável. Vulkan usa uma camada implícita por usuário. ETW pode exigir a conta no grupo Performance Log Users; processos elevados ou protegidos podem recusar acesso. Anti-cheats podem bloquear o hook; não há troca automática para janelas flutuantes."
            : "O renderer dentro do jogo não está disponível nesta plataforma. O modo selecionado não alterna automaticamente para janelas flutuantes.";

    private void PublishInGameOverlayFrame(bool initializeOrClear = false, bool forceHidden = false)
    {
        if (_disposed) return;
        // Ainda não há serviço de sensores durante o construtor; o frame é publicado após a inicialização.
        if (_sensorService is null) return;
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
        if (OperatingSystem.IsLinux())
        {
            var requestedMode = OverlayRenderMode;
            var shouldConfigure = requestedMode != _linuxConfiguredSessionMode;
            _linuxOverlayRegistrationDiagnostic = shouldConfigure
                ? requestedMode == "inGame"
                    ? "Preparando a integração Vulkan/OpenGL para esta sessão…"
                    : "Preparando captura passiva de FPS Vulkan/OpenGL…"
                : "";
            Raise(nameof(IsLinuxOverlaySessionWarningVisible));
            Raise(nameof(LinuxOverlaySessionDiagnosticText));
            if (shouldConfigure)
                _linuxOverlaySessionConfigurationTask = ConfigureLinuxOverlaySessionAsync(requestedMode);
            else
                _linuxOverlaySessionConfigurationTask = Task.CompletedTask;
        }

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

    private long _windowsOverlayLastScanTicks;

    private Task WaitForLinuxOverlaySessionConfigurationAsync()
        => _linuxOverlaySessionConfigurationTask;

    private async Task ConfigureLinuxOverlaySessionAsync(string requestedMode)
    {
        await _linuxOverlaySessionConfiguration.WaitAsync();
        try
        {
            LinuxOverlaySessionResult result;
            try
            {
                result = await Task.Run(() => requestedMode == "inGame"
                    ? LinuxOverlaySessionEnvironment.Enable()
                    : LinuxOverlaySessionEnvironment.EnableFpsCapture());
            }
            catch (Exception ex)
            {
                result = new(false, false, $"Falha ao configurar o ambiente Linux: {ex.Message}");
                ErrorLog.Write("InGameOverlay.LinuxSession", ex);
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (!_disposed && OverlayRenderMode == requestedMode)
                    {
                        _linuxOverlayRegistrationDiagnostic = result.Message;
                        if (result.SessionActive || result.Persistent)
                            _linuxConfiguredSessionMode = requestedMode;
                        Raise(nameof(IsLinuxOverlaySessionWarningVisible));
                        Raise(nameof(LinuxOverlaySessionDiagnosticText));
                        PublishInGameOverlayFrame();
                    }
                }
                finally
                {
                    completion.TrySetResult();
                }
            });
            await completion.Task;
        }
        finally
        {
            _linuxOverlaySessionConfiguration.Release();
        }
    }

    private async void OnInGameOverlayHeartbeat(object? sender, EventArgs e)
    {
        if (_disposed) return;
        PublishInGameOverlayFrame();
        if (!OperatingSystem.IsWindows() || OverlayRenderMode != "inGame" || _windowsOverlayProcessScanActive)
            return;

        // Com hooks já instalados a varredura de processos é espaçada (3 s); sem hooks mantém 1 s para reagir rápido a um jogo novo.
        var nowTicks = Environment.TickCount64;
        if (_windowsOverlayInjectedProcessIds.Count > 0 && nowTicks - _windowsOverlayLastScanTicks < 3000) return;
        _windowsOverlayLastScanTicks = nowTicks;
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

            // A Vulkan status alone does not mean that the Windows OpenGL/D3D hook
            // is installed: a process can load Vulkan and present through OpenGL.
            var alreadyPublished = InGameOverlayNativeStatusReader.WindowsHookProcessIds(
                InGameOverlayNativeStatusReader.ReadWindows(livePids));
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
            var hasInstalledVulkanLayer = LinuxOverlayNativeFiles.HasInstalledVulkanLayer
                || manifestCandidates.Any(File.Exists) && libraryCandidates.Any(File.Exists);
            var vulkanStatus = hasInstalledVulkanLayer ? "registrada para este usuário" : "não registrada";
            var openGlStatus = LinuxOverlayNativeFiles.HasInstalledOpenGlPreload ? "instalado" : "não instalado";
            var registrationDiagnostic = string.IsNullOrWhiteSpace(_linuxOverlayRegistrationDiagnostic)
                ? "Integração da sessão não confirmada."
                : _linuxOverlayRegistrationDiagnostic.TrimEnd('.');
            return $"Plataforma: Linux · camada Vulkan {vulkanStatus} · preload OpenGL {openGlStatus}. {registrationDiagnostic}";
        }

        if (OperatingSystem.IsWindows())
            return $"Plataforma: Windows · detecção global por ETW/PresentMon ativa no modo Dentro do jogo; hooks para Direct3D/OpenGL e Vulkan implícito por usuário. Jogos Vulkan abertos antes da ativação precisam ser reiniciados. {_windowsVulkanLayerDiagnostic} {WindowsOverlayHookService.GetNativeArtifactsDiagnostic()}";

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
                ? "API detectada: nenhum processo Vulkan/OpenGL publicou estado. Feche completamente o launcher e o jogo e abra-os novamente após ativar o modo. Se persistir, confira o backend gráfico em F3; Flatpak pode não herdar as bibliotecas do host."
                : "API detectada: nenhum processo detectado apresentando frames publicou estado do hook."
            : string.Join("\n", statuses.Select(status =>
                $"PID {status.ProcessId} · {status.Producer} · {status.ApiName} · {status.StateName}: {status.Message}"));
        var visibilityDiagnostic = OverlayVisible
            ? null
            : "Visibilidade: overlay oculto. Use Mostrar overlay na Visão geral ou o atalho configurado.";
        var etwDiagnostic = OperatingSystem.IsWindows()
            ? (_sensorService as IOverlayProcessDiscovery)?.OverlayDiscoveryDiagnostic
            : null;
        return string.Join("\n", new[] { nativeText,
            visibilityDiagnostic,
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
