using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PhantomMonitor.App.Services.Overlay;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

var tests = new (string Name, Action Run)[]
{
    ("coordenadas do canvas acompanham resolução e escala da tela", TestCanvasToScreenCoordinates),
    ("layout aplicado preserva itens e o filtro de séries do preview", TestAppliedContentParity),
    ("modo do overlay migra configs antigas para janela flutuante", TestOverlayRenderModeMigration),
    ("modo e executáveis do overlay persistem no config.json", TestOverlayRenderModePersistence),
    ("protocolo nativo rejeita quadros parciais e desatualizados", TestInGameFrameProtocol),
    ("canal POSIX publica leitura estável e somente leitura", TestPosixInGameFramePublisher)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS: {test.Name}");
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
    if (!OperatingSystem.IsLinux()) return;

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
