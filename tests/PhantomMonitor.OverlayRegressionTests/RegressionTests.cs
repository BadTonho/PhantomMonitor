/// <summary>Testes de regressão xUnit (antigo console app com PASS:/SKIP:). Os nomes de exibição preservam os originais.</summary>
public sealed class RegressionTests
{
    [Fact(DisplayName = "coordenadas do canvas acompanham resolução e escala da tela")]
    public void TestCanvasToScreenCoordinates() => OverlayRegressionCases.TestCanvasToScreenCoordinates();

    [Fact(DisplayName = "layout aplicado preserva itens e o filtro de séries do preview")]
    public void TestAppliedContentParity() => OverlayRegressionCases.TestAppliedContentParity();

    [Fact(DisplayName = "nomes de métricas são personalizados por bloco e voltam ao padrão")]
    public void TestOverlayMetricNames() => OverlayRegressionCases.TestOverlayMetricNames();

    [Fact(DisplayName = "modelo da GPU é opcional sem afetar outros dispositivos")]
    public void TestOverlayGpuModel() => OverlayRegressionCases.TestOverlayGpuModel();

    [Fact(DisplayName = "configuração de nomes e modelo da GPU sobrevive ao reinício")]
    public void TestOverlayNamesPersistence() => OverlayRegressionCases.TestOverlayNamesPersistence();

    [Fact(DisplayName = "linha personalizada mantém referências e configurações do bloco")]
    public void TestCustomLine() => OverlayRegressionCases.TestCustomLine();

    [Fact(DisplayName = "seleção de hardware insere somente após escolha explícita")]
    public void TestHardwareSelection() => OverlayRegressionCases.TestHardwareSelection();

    [Fact(DisplayName = "inventário Linux identifica CPU, placa-mãe e GPUs por PCI")]
    public void TestLinuxHardwareNames() => OverlayRegressionCases.TestLinuxHardwareNames();

    [Fact(DisplayName = "protocolo aceita largura de linha na versão 2")]
    public void TestCustomLineFrame() => OverlayRegressionCases.TestCustomLineFrame();

    [Fact(DisplayName = "modo do overlay migra configs antigas para janela flutuante")]
    public void TestOverlayRenderModeMigration() => OverlayRegressionCases.TestOverlayRenderModeMigration();

    [Fact(DisplayName = "modo e executáveis do overlay persistem no config.json")]
    public void TestOverlayRenderModePersistence() => OverlayRegressionCases.TestOverlayRenderModePersistence();

    [Fact(DisplayName = "protocolo nativo rejeita quadros parciais e desatualizados")]
    public void TestInGameFrameProtocol() => OverlayRegressionCases.TestInGameFrameProtocol();

    [Fact(DisplayName = "diagnóstico nativo valida sequência, versão, idade e UTF-8")]
    public void TestNativeOverlayStatusProtocol() => OverlayRegressionCases.TestNativeOverlayStatusProtocol();

    [Fact(DisplayName = "estado Vulkan não impede instalar hook OpenGL do mesmo processo")]
    public void TestWindowsHookProducerFiltering() => OverlayRegressionCases.TestWindowsHookProducerFiltering();

    [LinuxFact(DisplayName = "canal POSIX publica leitura estável e somente leitura")]
    public void TestPosixInGameFramePublisher() => OverlayRegressionCases.TestPosixInGameFramePublisher();

    [Fact(DisplayName = "PresentMon descobre múltiplos PIDs e ignora linhas inválidas")]
    public void TestPresentMonProcessDiscovery() => OverlayRegressionCases.TestPresentMonProcessDiscovery();

    [Fact(DisplayName = "PIDs sem novos frames expiram da descoberta")]
    public void TestPresentMonProcessExpiration() => OverlayRegressionCases.TestPresentMonProcessExpiration();

    [Fact(DisplayName = "sessão PresentMon continua ativa para descoberta com coleta parada")]
    public void TestPresentMonSessionDemand() => OverlayRegressionCases.TestPresentMonSessionDemand();

    [Fact(DisplayName = "registro Vulkan x86/x64 pode ser ativado e removido sem Registro real")]
    public void TestVulkanLayerRegistration() => OverlayRegressionCases.TestVulkanLayerRegistration();

    [Fact(DisplayName = "temperatura da placa-mae distingue ausencia de zero")]
    public void TestMotherboardTemperatureAvailability() => OverlayRegressionCases.TestMotherboardTemperatureAvailability();

    [Fact(DisplayName = "captura de atalhos valida teclas e modificadores")]
    public void TestHotkeyCapture() => OverlayRegressionCases.TestHotkeyCapture();

    [Fact(DisplayName = "X11 considera combinacoes de travas do teclado")]
    public void TestX11HotkeyMasks() => OverlayRegressionCases.TestX11HotkeyMasks();

    [Fact(DisplayName = "portal Wayland interpreta preferencias e respostas")]
    public void TestWaylandHotkeyResponses() => OverlayRegressionCases.TestWaylandHotkeyResponses();

    [Fact(DisplayName = "falha ao salvar restaura a combinacao anterior")]
    public void TestHotkeyRollback() => OverlayRegressionCases.TestHotkeyRollback();

    [WindowsFact(DisplayName = "troca de atalho Windows preserva pausa e combinacao anterior")]
    public void TestOverlayHotkeyWindows() => OverlayRegressionCases.TestOverlayHotkeyWindows();

    [Fact(DisplayName = "alertas com a mesma condição preservam ações e cooldown independentes")]
    public void Alerts() => CoreRegressionTests.Alerts();

    [Fact(DisplayName = "série que reaparece descarta cadência e suavização antigas")]
    public void Tuning() => CoreRegressionTests.Tuning();

    [Fact(DisplayName = "configuração filtra gráficos/templates nulos antes de normalizar")]
    public void Config() => CoreRegressionTests.Config();

    [Fact(DisplayName = "configuração portátil usa diretório próprio e preserva instalações")]
    public void ConfigLocations() => CoreRegressionTests.ConfigLocations();

    [Fact(DisplayName = "CSV abre segmento com novas séries e preserva registros anteriores")]
    public void Csv() => CoreRegressionTests.Csv();

    [Fact(DisplayName = "fila de escrita permanece limitada quando o consumidor bloqueia")]
    public void WriterQueue() => CoreRegressionTests.WriterQueue();

    [Fact(DisplayName = "rede Linux conta bytes sem multiplicar intervalos ociosos")]
    public void Network() => CoreRegressionTests.Network();

    [Fact(DisplayName = "discos Linux são selecionados por sysfs, incluindo virtio e eMMC")]
    public void Disks() => CoreRegressionTests.Disks();

    [Fact(DisplayName = "autostart resolve AppImage persistente e configuração XDG")]
    public void Startup() => CoreRegressionTests.Startup();

    [Fact(DisplayName = "ring Vulkan preserva frames e rejeita leitura parcial")]
    public void VulkanRing() => CoreRegressionTests.VulkanRing();

    [Fact(DisplayName = "FPS Linux troca processo ocioso e reinicia contadores por sessão")]
    public void FpsSessions() => CoreRegressionTests.FpsSessions();

    [Fact(DisplayName = "extração ZIP preserva subpastas e rejeita traversal")]
    public void Zip() => CoreRegressionTests.Zip();

    [Fact(DisplayName = "extração tar Linux suporta espaços e apóstrofos sem shell")]
    public void Tar() => CoreRegressionTests.Tar();

    [Fact(DisplayName = "download rejeita SHA-256 ausente, inválido ou divergente")]
    public void Digest() => CoreRegressionTests.Digest();

    [WindowsFact(DisplayName = "atalho Windows registra na thread proprietária")]
    public void Hotkeys() => CoreRegressionTests.Hotkeys();

    [WindowsFact(DisplayName = "updater Windows instala subpastas e restaura todos os arquivos após falha")]
    public void UpdateTransaction() => CoreRegressionTests.UpdateTransaction();
}
