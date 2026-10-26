using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using PhantomMonitor.App.Services.Overlay;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.App.Services.Hotkeys;
using PhantomMonitor.Models;
using Avalonia.Input;
using Tmds.DBus.Protocol;
using PhantomMonitor.Services;

(string Name, Action Run)[] tests =
[
    ("coordenadas do canvas acompanham resolução e escala da tela", TestCanvasToScreenCoordinates),
    ("layout aplicado preserva itens e o filtro de séries do preview", TestAppliedContentParity),
    ("modo do overlay migra configs antigas para janela flutuante", TestOverlayRenderModeMigration),
    ("modo e executáveis do overlay persistem no config.json", TestOverlayRenderModePersistence),
    ("protocolo nativo rejeita quadros parciais e desatualizados", TestInGameFrameProtocol),
    ("diagnóstico nativo valida sequência, versão, idade e UTF-8", TestNativeOverlayStatusProtocol),
    ("canal POSIX publica leitura estável e somente leitura", TestPosixInGameFramePublisher),
    ("PresentMon descobre múltiplos PIDs e ignora linhas inválidas", TestPresentMonProcessDiscovery),
    ("PIDs sem novos frames expiram da descoberta", TestPresentMonProcessExpiration),
    ("sessão PresentMon continua ativa para descoberta com coleta parada", TestPresentMonSessionDemand),
    ("registro Vulkan x86/x64 pode ser ativado e removido sem Registro real", TestVulkanLayerRegistration),
    ("temperatura da placa-mae distingue ausencia de zero", TestMotherboardTemperatureAvailability),
    ("captura de atalhos valida teclas e modificadores", TestHotkeyCapture),
    ("X11 considera combinacoes de travas do teclado", TestX11HotkeyMasks),
    ("portal Wayland interpreta preferencias e respostas", TestWaylandHotkeyResponses),
    ("falha ao salvar restaura a combinacao anterior", TestHotkeyRollback),
    ("troca de atalho Windows preserva pausa e combinacao anterior", TestOverlayHotkeyWindows),
    .. CoreRegressionTests.Cases
];

var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS: {test.Name}"); }
    catch (TestSkippedException ex) { Console.WriteLine($"SKIP: {test.Name}: {ex.Message}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL: {test.Name}: {ex}"); }
}
Environment.ExitCode = failed > 0 ? 1 : 0;

static void TestMotherboardTemperatureAvailability()
{
    var data = new SensorData();
    Assert(MainWindowViewModel.FormatMotherboardTemperature(data) == "Indispon\u00edvel", "Leitura ausente deve ser indicada.");
    data.SetValue(SensorType.MotherboardTemp, 0);
    Assert(MainWindowViewModel.FormatMotherboardTemperature(data) == "0 \u00b0C", "Zero e uma leitura valida.");
}

static void TestHotkeyCapture()
{
    Assert(HotkeyCapture.TryFormat(Key.O, KeyModifiers.Control | KeyModifiers.Shift, out var key)
        && key == "Ctrl+Shift+O", "Combinação capturada incorretamente.");
    Assert(HotkeyCapture.TryFormat(Key.F24, KeyModifiers.None, out key) && key == "F24", "F24 deve ser aceito.");
    Assert(!HotkeyCapture.TryFormat(Key.A, KeyModifiers.None, out _), "Letra isolada deve ser recusada.");
    Assert(!HotkeyCapture.TryFormat(Key.Enter, KeyModifiers.Control, out _), "Tecla não suportada deve ser recusada.");
    Assert(HotkeyCapture.SameCombination("Shift+Ctrl+O", "Ctrl+Shift+O"), "Ordem dos modificadores não altera a combinação.");
}

static void TestX11HotkeyMasks()
{
    var masks = X11OverlayHotkeyBackend.BuildMasks(4, [2, 16, 128]);
    Assert(masks.SequenceEqual(new ushort[] { 4, 6, 20, 22, 132, 134, 148, 150 }), "Travas X11 devem incluir todas as combinações.");
    Assert(X11OverlayHotkeyBackend.BuildMasks(4, [0, 2, 2]).SequenceEqual(new ushort[] { 4, 6 }), "Máscaras repetidas não devem duplicar registros.");
    var repeat = new X11KeyRepeatFilter();
    Assert(repeat.Press(10), "Primeiro pressionamento deve ativar.");
    repeat.Release(20, 0);
    Assert(!repeat.Press(20), "Repetição automática não deve alternar novamente.");
    repeat.Release(30, 100);
    repeat.FlushRelease(161);
    Assert(repeat.Press(40), "Um novo pressionamento deve ativar após soltar a tecla.");
}

static void TestWaylandHotkeyResponses()
{
    Assert(WaylandOverlayHotkeyBackend.ToPortalTrigger(HotkeyParser.ModControl | HotkeyParser.ModShift, 'O') == "CTRL+SHIFT+o", "Formato do portal inválido.");
    Assert(WaylandOverlayHotkeyBackend.ToPortalTrigger(HotkeyParser.ModWin, 0x87) == "LOGO+F24", "Tecla de função inválida.");
    Assert(!WaylandOverlayHotkeyBackend.ReadBinding(1, new Dictionary<string, VariantValue>()).Success, "Cancelamento não pode ativar atalho.");
    Assert(!WaylandOverlayHotkeyBackend.SelectBinding(0, []).Success, "Lista vazia não confirma registro.");
    var accepted = WaylandOverlayHotkeyBackend.SelectBinding(0, [("toggle-overlay", "Meta+F10")]);
    Assert(accepted.Success && accepted.Display == "Meta+F10", "Descrição efetiva do portal deve ser exibida.");
}

static void TestOverlayHotkeyWindows()
{
    if (!OperatingSystem.IsWindows()) throw new TestSkippedException("Requer Windows.");
    using var service = new GlobalHotkeyService();
    using var other = new GlobalHotkeyService();
    using var backend = new WindowsOverlayHotkeyBackend(service, () => { });
    const string first = "Ctrl+Alt+Shift+F22";
    const string conflict = "Ctrl+Alt+Shift+F23";
    const string pause = "Ctrl+Alt+Shift+F24";
    Assert(service.Register(GlobalHotkeyService.PauseHotkeyId, pause, () => { }), "Não foi possível registrar pausa de teste.");
    Assert(backend.ApplyAsync(first).GetAwaiter().GetResult().Success, "Registro inicial falhou.");
    Assert(other.Register(1, conflict, () => { }), "Não foi possível preparar conflito de teste.");
    Assert(!backend.ApplyAsync(conflict).GetAwaiter().GetResult().Success, "Conflito deveria ser informado.");
    Assert(!other.Register(2, first, () => { }), "Atalho anterior deve permanecer registrado.");
    Assert(backend.ApplyAsync("").GetAwaiter().GetResult().Success, "Limpeza falhou.");
    Assert(other.Register(2, first, () => { }), "Atalho removido deve ficar disponível.");
    Assert(!other.Register(3, pause, () => { }), "Limpar overlay não pode remover atalho de pausa.");
}

static void TestHotkeyRollback()
{
    var active = "Ctrl+F10";
    Task<HotkeyResult> Apply(string value)
    {
        if (value == "Ctrl+F12") return Task.FromResult(HotkeyResult.Failed("ocupado"));
        active = value;
        return Task.FromResult(HotkeyResult.Active(value));
    }
    var failed = new HotkeyTransition(active, Apply);
    Assert(!failed.StageAsync("Ctrl+F12").GetAwaiter().GetResult().Success && active == "Ctrl+F10", "Conflito deve manter atalho anterior.");
    var staged = new HotkeyTransition(active, Apply);
    Assert(staged.StageAsync("Ctrl+F11").GetAwaiter().GetResult().Success && active == "Ctrl+F11", "Novo atalho deveria ficar ativo.");
    Assert(staged.RollbackAsync().GetAwaiter().GetResult().Success && active == "Ctrl+F10", "Falha na gravação deve restaurar o anterior.");
}

static void TestCanvasToScreenCoordinates()
{
    var screens = new (int Width, int Height, double Scale, int OriginX, int OriginY)[]
    {
        (1920, 1080, 1, 0, 0),
        (2560, 1440, 1.25, -1920, 0),
        (3840, 2160, 2, 0, -2160),
        (3440, 1440, 1.5, -3440, 1080)
    };
    const double canvasWidth = 1080;
    const double canvasHeight = 680;
    const double blockWidthDip = 220;
    const double blockHeightDip = 96;
    var fractions = new[] { 0d, 0.17, 0.5, 0.83, 1d };

    foreach (var screen in screens)
    {
        var screenWidthDip = screen.Width / screen.Scale;
        var screenHeightDip = screen.Height / screen.Scale;
        var previewWidth = blockWidthDip * canvasWidth / screenWidthDip;
        var previewHeight = blockHeightDip * canvasHeight / screenHeightDip;

        foreach (var fraction in fractions)
        {
            var previewX = fraction * (canvasWidth - previewWidth);
            var normalizedX = OverlayPositionMath.ToNormalizedFromDisplay(previewX, canvasWidth,
                previewWidth, OverlayPositionMath.ReferenceWidth);
            var actualX = OverlayPositionMath.ToPixelCoordinate(normalizedX, screen.OriginX,
                screen.Width, blockWidthDip, screen.Scale);
            var expectedX = screen.OriginX + (int)Math.Round(
                fraction * (screen.Width - blockWidthDip * screen.Scale), MidpointRounding.AwayFromZero);
            Assert(Math.Abs(actualX - expectedX) <= 1,
                $"X: tela {screen.Width}×{screen.Height} escala {screen.Scale}, fração {fraction}, esperado {expectedX}, recebido {actualX}");

            var previewY = fraction * (canvasHeight - previewHeight);
            var normalizedY = OverlayPositionMath.ToNormalizedFromDisplay(previewY, canvasHeight,
                previewHeight, OverlayPositionMath.ReferenceHeight);
            var actualY = OverlayPositionMath.ToPixelCoordinate(normalizedY, screen.OriginY,
                screen.Height, blockHeightDip, screen.Scale);
            var expectedY = screen.OriginY + (int)Math.Round(
                fraction * (screen.Height - blockHeightDip * screen.Scale), MidpointRounding.AwayFromZero);
            Assert(Math.Abs(actualY - expectedY) <= 1,
                $"Y: tela {screen.Width}×{screen.Height} escala {screen.Scale}, fração {fraction}, esperado {expectedY}, recebido {actualY}");
        }
    }
}

static void TestPresentMonProcessDiscovery()
{
    var parser = new PresentMonCsvParser();
    Assert(parser.TryParseLine("ProcessID,Application,msBetweenPresents", out var header) && header is null,
        "PresentMon CSV header should be accepted.");
    var tracker = new RecentPresentingProcessTracker(TimeSpan.FromSeconds(5));
    var now = DateTimeOffset.UtcNow;
    var rows = new[]
    {
        "101,game-one.exe,16.6",
        "202,game-two.exe,8.3",
        "-1,invalid.exe,16.6",
        "303,broken-time.exe,NaN",
        "404,zero-time.exe,0",
        "505,explorer.exe,16.6"
    };
    foreach (var row in rows)
    {
        if (parser.TryParseLine(row, out var frame) && frame is not null
            && !WindowsOverlayProcessFilter.IsIgnored(frame.Application))
            tracker.Observe(frame.ProcessId, frame.Application, now);
    }

    var pids = tracker.Snapshot(now).Select(process => process.ProcessId).ToArray();
    Assert(pids.SequenceEqual(new[] { 101, 202 }),
        $"Expected only the two valid presenting applications, got {string.Join(",", pids)}.");
}

static void TestPresentMonProcessExpiration()
{
    var tracker = new RecentPresentingProcessTracker(TimeSpan.FromSeconds(5));
    var now = DateTimeOffset.UtcNow;
    tracker.Observe(101, "game.exe", now);
    Assert(tracker.Snapshot(now.AddSeconds(4)).Count == 1, "Recent presenters should remain available during the TTL.");
    Assert(tracker.Snapshot(now.AddSeconds(6)).Count == 0, "Processes with no recent frames should expire.");
}

static void TestPresentMonSessionDemand()
{
    var demand = new PresentMonSessionDemand();
    demand.SetDiscoveryActive(true);
    Assert(demand.IsRequired, "In-game discovery should start the shared PresentMon session without sensor collection.");
    demand.SetCollectionActive(true);
    demand.SetCollectionActive(false);
    Assert(demand.IsRequired && demand.DiscoveryActive,
        "Stopping sensor collection must leave PresentMon running while overlay discovery is active.");
    demand.SetDiscoveryActive(false);
    Assert(!demand.IsRequired, "PresentMon should stop after both consumers are disabled.");
}

#pragma warning disable CA1416 // Registro simulado; nenhum acesso ao Windows Registry ocorre no teste.
static void TestVulkanLayerRegistration()
{
    var root = Path.Combine(Path.GetTempPath(), $"phantom-vulkan-regression-{Guid.NewGuid():N}");
    var registry = new FakeVulkanLayerRegistry();
    try
    {
        foreach (var architecture in new[] { "x64", "x86" })
        {
            var directory = Path.Combine(root, "PhantomOverlay", architecture);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "phantom_vulkan_layer.json"), "{}");
        }

        var registration = new WindowsVulkanImplicitLayerRegistration(registry);
        var enabled = registration.Enable(root);
        var expectedViews = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Registry32 };
        Assert(enabled.Contains("ativa", StringComparison.OrdinalIgnoreCase), "Registration should report success.");
        Assert(registry.Entries.Select(entry => entry.View).Order().SequenceEqual(expectedViews.Order()),
            "x86 and x64 manifests must be registered in their matching registry views.");
        Assert(registry.Entries.All(entry => Path.IsPathFullyQualified(entry.ManifestPath)),
            "Vulkan registry value names must be absolute manifest paths.");

        registry.Entries.Add(new VulkanLayerRegistryEntry(RegistryView.Registry32,
            Path.Combine(root, "old-install", "PhantomOverlay", "x86", "phantom_vulkan_layer.json")));
        var removed = registration.Disable(root);
        Assert(removed.Contains("removida", StringComparison.OrdinalIgnoreCase), "Removal should report success.");
        Assert(registry.Entries.Count == 0, "Disable should remove active and stale registrations through the test writer.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
#pragma warning restore CA1416

static void TestAppliedContentParity()
{
    var draft = new OverlayBlockConfig
    {
        Id = "regression-block",
        X = 0.37,
        Y = 0.62,
        Items =
        [
            new() { Kind = "category", Value = "gpu" },
            new() { Kind = "series", Value = "Gpu*" },
            new() { Kind = "frametime", Value = "frametime" },
            new() { Kind = "template", Value = "summary" }
        ],
        MetricOrder = new Dictionary<string, List<string>>
        {
            ["category:gpu"] = ["GpuCore", "GpuTemperature"]
        }
    };

    var applied = MainWindowViewModel.CloneOverlayBlock(draft);
    Assert(JsonSerializer.Serialize(draft) == JsonSerializer.Serialize(applied),
        "Save & Apply must copy the full block config, including content order and normalized position.");

    var identities = new[] { "GpuCore", "GpuCore#1", "GpuTemperature#1", "CpuCore#1" };
    IReadOnlyList<string> activeProfileSeries = ["GpuCore#1", "GpuTemperature#1"];
    var previewContent = OverlayBlockContentRules.SelectSeriesIdentities(identities, "Gpu*", activeProfileSeries);
    var appliedContent = OverlayBlockContentRules.SelectSeriesIdentities(identities, "Gpu*", activeProfileSeries);
    Assert(previewContent.SequenceEqual(appliedContent, StringComparer.OrdinalIgnoreCase),
        "Draft preview and applied blocks must use the same profile series filter.");
    Assert(previewContent.SequenceEqual(activeProfileSeries, StringComparer.OrdinalIgnoreCase),
        "The shared profile filter must retain exactly the enabled per-device series.");
}

static void TestOverlayRenderModeMigration()
{
    var legacy = new AppConfig();
    legacy.Normalize();
    Assert(legacy.Overlay.RenderMode == "floating", "Configs sem renderMode devem continuar no modo flutuante.");

    var invalid = new AppConfig { Overlay = new() { RenderMode = "unknown" } };
    invalid.Normalize();
    Assert(invalid.Overlay.RenderMode == "floating", "Um renderMode desconhecido deve voltar ao modo seguro padrão.");

    var inGame = new AppConfig
    {
        Overlay = new()
        {
            RenderMode = "INGAME",
            InGameTargets =
            [
                new() { ExecutablePath = " /games/demo.x86_64 ", Arguments = " --fullscreen " },
                new() { ExecutablePath = "/games/demo.x86_64" },
                new() { ExecutablePath = " " }
            ]
        }
    };
    inGame.Normalize();
    Assert(inGame.Overlay.RenderMode == "inGame", "O modo dentro do jogo deve ser normalizado sem diferenciar maiúsculas.");
    Assert(inGame.Overlay.InGameTargets.Count == 1
        && inGame.Overlay.InGameTargets[0].ExecutablePath == "/games/demo.x86_64"
        && inGame.Overlay.InGameTargets[0].Arguments == "--fullscreen",
        "Alvos devem ser limpos, deduplicados e manter argumentos.");
}

static void TestOverlayRenderModePersistence()
{
    var path = Path.Combine(Path.GetTempPath(), $"phantom-overlay-config-{Guid.NewGuid():N}.json");
    try
    {
        var config = new AppConfig
        {
            Overlay = new()
            {
                RenderMode = "inGame",
                InGameTargets = [new() { ExecutablePath = "/games/demo", Arguments = "--fullscreen" }]
            }
        };
        Assert(config.Save(path), "A configuração de teste precisa ser salva.");
        var json = File.ReadAllText(path);
        Assert(json.Contains("\"renderMode\": \"inGame\"", StringComparison.Ordinal),
            "O JSON deve persistir overlay.renderMode.");
        Assert(json.Contains("\"inGameTargets\"", StringComparison.Ordinal),
            "O JSON deve persistir a lista de executáveis.");

        var loaded = AppConfig.Load(path);
        Assert(loaded.Overlay.RenderMode == "inGame"
            && loaded.Overlay.InGameTargets.Count == 1
            && loaded.Overlay.InGameTargets[0].ExecutablePath == "/games/demo"
            && loaded.Overlay.InGameTargets[0].Arguments == "--fullscreen",
            "A escolha global e os executáveis devem sobreviver a salvar e carregar.");
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
    }
}

static void TestInGameFrameProtocol()
{
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var payloadBytes = CreateSamplePayload();
    var frame = CreateNativeFrame(payloadBytes, sequence: 2, timestamp: now);
    Assert(InGameOverlayFrameProtocol.TryCopyStableFrame(frame, now, TimeSpan.FromSeconds(3), out var copied)
        && copied.SequenceEqual(payloadBytes), "Um quadro completo, consistente e recente deve ser aceito.");

    var partial = CreateNativeFrame(payloadBytes, sequence: 2, timestamp: now)[..^1];
    Assert(!InGameOverlayFrameProtocol.TryCopyStableFrame(partial, now, TimeSpan.FromSeconds(3), out _),
        "O leitor deve rejeitar um mapeamento menor que o tamanho declarado.");

    var writing = CreateNativeFrame(payloadBytes, sequence: 3, timestamp: now);
    Assert(!InGameOverlayFrameProtocol.TryCopyStableFrame(writing, now, TimeSpan.FromSeconds(3), out _),
        "Uma sequência ímpar identifica escrita em andamento e não deve ser desenhada.");

    var stale = CreateNativeFrame(payloadBytes, sequence: 2, timestamp: now - 5000);
    Assert(!InGameOverlayFrameProtocol.TryCopyStableFrame(stale, now, TimeSpan.FromSeconds(3), out _),
        "O leitor deve rejeitar quadros além do limite de idade.");

    var payloadText = Encoding.UTF8.GetString(payloadBytes);
    var incompletePayload = Encoding.UTF8.GetBytes(payloadText[..payloadText.LastIndexOf("E\n", StringComparison.Ordinal)]);
    var incomplete = CreateNativeFrame(incompletePayload, sequence: 2, timestamp: now);
    Assert(!InGameOverlayFrameProtocol.TryCopyStableFrame(incomplete, now, TimeSpan.FromSeconds(3), out _),
        "Um payload sem encerramento do bloco deve ser rejeitado mesmo que o header esteja estável.");

    var unknownVersion = CreateNativeFrame(payloadBytes, sequence: 2, timestamp: now);
    BinaryPrimitives.WriteUInt32LittleEndian(unknownVersion.AsSpan(4), 99);
    Assert(!InGameOverlayFrameProtocol.TryCopyStableFrame(unknownVersion, now, TimeSpan.FromSeconds(3), out _),
        "Versões de protocolo desconhecidas devem ser ignoradas.");
}

static void TestPosixInGameFramePublisher()
{
    if (!OperatingSystem.IsLinux()) throw new TestSkippedException("Requer Linux.");

    var name = InGameOverlayFramePublisher.LinuxMappingNamePrefix
        + NativeMethods.getuid().ToString(System.Globalization.CultureInfo.InvariantCulture);
    var path = "/dev/shm/" + name.TrimStart('/');
    NativeMethods.shm_unlink(name); // limpa apenas o objeto reservado a este teste.
    try
    {
        var publisher = new InGameOverlayFramePublisher();
        (long Sequence, byte[] Payload) first;
        try
        {
            Assert(publisher.Publish(CreateSamplePayload()), "O publisher deve criar e atualizar a região POSIX.");
            Assert(File.Exists(path), "O objeto de memória compartilhada deve aparecer em /dev/shm.");
            var permissions = File.GetUnixFileMode(path);
            Assert((permissions & UnixFileMode.UserRead) != 0 && (permissions & UnixFileMode.UserWrite) == 0,
                "Novos processos devem ter permissão de leitura, sem permissão de escrita.");
            first = ReadPublishedFrame(name);
        }
        finally
        {
            publisher.Dispose();
        }

        Assert(File.Exists(path), "O nome da região deve permanecer estável para renderizadores já conectados.");
        var restartedPublisher = new InGameOverlayFramePublisher();
        try
        {
            Assert(restartedPublisher.Publish(CreateSamplePayload()), "O publisher deve reabrir o canal após reiniciar.");
            var next = ReadPublishedFrame(name);
            Assert(next.Sequence > first.Sequence && next.Payload.SequenceEqual(first.Payload),
                "A sequência deve avançar e o mesmo mapa deve receber o quadro da nova execução.");
        }
        finally
        {
            restartedPublisher.Dispose();
        }
    }
    finally
    {
        NativeMethods.shm_unlink(name);
    }

    Assert(!File.Exists(path), "O teste remove a região persistente depois de validar o ciclo de reinício.");
}

static void TestNativeOverlayStatusProtocol()
{
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var bytes = CreateNativeStatusSnapshot(now);
    Assert(InGameOverlayNativeStatusReader.TryParseSnapshot(bytes, out var status)
        && status.ProcessId == 42 && status.Api == 1 && status.State == 1
        && status.Message == "Renderer Vulkan ativo",
        "Um diagnóstico nativo válido deve manter o PID, API, estado e mensagem.");

    Assert(!InGameOverlayNativeStatusReader.TryParseSnapshot(bytes[..^1], out _),
        "Um mapeamento de diagnóstico parcial deve ser rejeitado.");

    var inProgress = CreateNativeStatusSnapshot(now, sequence: 3);
    Assert(!InGameOverlayNativeStatusReader.TryParseSnapshot(inProgress, out _),
        "Uma sequência ímpar indica que o produtor ainda escreve o diagnóstico.");

    var stale = CreateNativeStatusSnapshot(now - 31_000);
    Assert(!InGameOverlayNativeStatusReader.TryParseSnapshot(stale, out _),
        "Um diagnóstico com mais de 30 segundos deve ser rejeitado.");

    var unknownVersion = CreateNativeStatusSnapshot(now);
    BinaryPrimitives.WriteUInt32LittleEndian(unknownVersion.AsSpan(4), 99);
    Assert(!InGameOverlayNativeStatusReader.TryParseSnapshot(unknownVersion, out _),
        "Uma versão desconhecida do diagnóstico deve ser rejeitada.");

    var malformedUtf8 = CreateNativeStatusSnapshot(now);
    malformedUtf8[36] = 0xFF;
    Assert(!InGameOverlayNativeStatusReader.TryParseSnapshot(malformedUtf8, out _),
        "Mensagem de diagnóstico com UTF-8 inválido deve ser rejeitada.");
}

static byte[] CreateNativeStatusSnapshot(long timestamp, long sequence = 2)
{
    var bytes = new byte[InGameOverlayNativeStatusReader.Capacity];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, InGameOverlayNativeStatusReader.Magic);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), InGameOverlayNativeStatusReader.Version);
    BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), sequence);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 42);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 1);
    BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(28), timestamp);
    Encoding.UTF8.GetBytes("Renderer Vulkan ativo").CopyTo(bytes, 36);
    return bytes;
}

static (long Sequence, byte[] Payload) ReadPublishedFrame(string name)
{
    var fd = NativeMethods.shm_open(name, 0, 0); // O_RDONLY
    Assert(fd >= 0, "Outro processo deve conseguir abrir o mapa como somente leitura.");
    try
    {
        var length = InGameOverlayFramePublisher.Capacity;
        var address = NativeMethods.mmap(IntPtr.Zero, (UIntPtr)length, 1, 1, fd, 0); // PROT_READ | MAP_SHARED
        Assert(address != new IntPtr(-1), "O mapa deve aceitar uma projeção de leitura.");
        try
        {
            var copy = new byte[length];
            Marshal.Copy(address, copy, 0, copy.Length);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert(InGameOverlayFrameProtocol.TryCopyStableFrame(copy, now, TimeSpan.FromSeconds(3), out var payload),
                "O leitor deve receber um quadro recente e consistente do publisher real.");
            return (BinaryPrimitives.ReadInt64LittleEndian(copy.AsSpan(8)), payload);
        }
        finally
        {
            NativeMethods.munmap(address, (UIntPtr)length);
        }
    }
    finally
    {
        NativeMethods.close(fd);
    }
}

static byte[] CreateSamplePayload()
{
    var text64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("Segoe UI"));
    var title64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("CPU"));
    var value64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("27%"));
    var payload = string.Join('\n',
        $"F\t1\t1\t0\t220\t1920\t1080\t11.5\t0.85\tFF141A24\t1\tFF2C527A\tFF1E90FF\t0\t{text64}\t{text64}\t9.5\t8\t8\t8\t8\t12\t12\t12\t12\t8\t0\t2\t0\t2\t24",
        $"B\t{title64}\t0.25\t0.5\t{title64}",
        $"T\t11.5\tFFF2F5FA\t{value64}",
        "G\t24\tFF1E90FF\t\t\t1,2,3",
        "E",
        "");
    return Encoding.UTF8.GetBytes(payload);
}

static byte[] CreateNativeFrame(byte[] payload, long sequence, long timestamp)
{
    var frame = new byte[InGameOverlayFrameProtocol.HeaderSize + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(frame, InGameOverlayFrameProtocol.Magic);
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), InGameOverlayFrameProtocol.Version);
    BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8), sequence);
    BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16), timestamp);
    BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24), payload.Length);
    payload.CopyTo(frame.AsSpan(InGameOverlayFrameProtocol.HeaderSize));
    return frame;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static class NativeMethods
{
    [DllImport("libc", SetLastError = true)]
    internal static extern int shm_open(string name, int flags, uint mode);
    [DllImport("libc", SetLastError = true)]
    internal static extern IntPtr mmap(IntPtr address, UIntPtr length, int protection, int flags, int fd, long offset);
    [DllImport("libc", SetLastError = true)]
    internal static extern int munmap(IntPtr address, UIntPtr length);
    [DllImport("libc", SetLastError = true)]
    internal static extern int close(int fd);
    [DllImport("libc", SetLastError = true)]
    internal static extern int shm_unlink(string name);
    [DllImport("libc")]
    internal static extern uint getuid();
}

sealed class FakeVulkanLayerRegistry : IVulkanLayerRegistryWriter
{
    public List<VulkanLayerRegistryEntry> Entries { get; } = [];

    public void Register(VulkanLayerRegistryEntry entry)
    {
        Entries.RemoveAll(current => current.View == entry.View && current.ManifestPath == entry.ManifestPath);
        Entries.Add(entry);
    }

    public void Remove(VulkanLayerRegistryEntry entry) =>
        Entries.RemoveAll(current => current.View == entry.View && current.ManifestPath == entry.ManifestPath);

    public IReadOnlyList<VulkanLayerRegistryEntry> FindOwnedEntries() => Entries.ToArray();
}
