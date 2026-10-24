using Avalonia.Threading;
using PhantomMonitor.App.Services.Overlay;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly InGameOverlayFramePublisher _inGameOverlayFramePublisher = new();
    private string _inGameOverlayDiagnosticText = "Canal nativo ainda não foi aberto.";
    private bool _inGameFrameChannelActive;
    private DispatcherTimer? _inGameOverlayHeartbeat;

    public string InGameOverlayDiagnosticText
    {
        get => _inGameOverlayDiagnosticText;
        private set => Set(ref _inGameOverlayDiagnosticText, value);
    }

    public bool IsInGameOverlayDiagnosticsVisible => OverlayRenderMode == "inGame";
    public string InGameOverlayCapabilityNotice => OperatingSystem.IsLinux()
        ? "Linux: a camada Vulkan disponível neste projeto coleta FPS, mas ainda não desenha o OSD no quadro. O modo dentro do jogo oculta as janelas flutuantes e não faz fallback automático."
        : OperatingSystem.IsWindows()
            ? "Windows: o hook Direct3D ainda não está disponível neste build. O modo dentro do jogo oculta as janelas flutuantes e não faz fallback automático."
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
            InGameOverlayDiagnosticText = $"{GetInGameOverlayPlatformStatus()}\n{_inGameOverlayFramePublisher.Status}\nAPI detectada: indisponível enquanto o hook de apresentação não estiver conectado. Renderer in-frame: indisponível neste build; nenhuma janela flutuante será aberta automaticamente.";
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

    private void OnInGameOverlayHeartbeat(object? sender, EventArgs e)
        => PublishInGameOverlayFrame();

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
            return $"Plataforma: Linux · camada Vulkan {(manifestCandidates.Any(File.Exists) && libraryCandidates.Any(File.Exists) ? "instalada" : "não encontrada")}; estado do jogo: não detectado.";
        }

        if (OperatingSystem.IsWindows())
            return $"Plataforma: Windows · executáveis autorizados: {OverlayInGameTargets.Count}; hook D3D11/12: não instalado neste build.";

        return "Plataforma: sem integração nativa para overlay dentro do jogo.";
    }
}
