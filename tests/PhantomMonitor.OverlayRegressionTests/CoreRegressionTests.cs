using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using PhantomMonitor.Models;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Linux;
using PhantomMonitor.Services.Update;

internal sealed class TestSkippedException(string message) : Exception(message);

internal static class CoreRegressionTests
{
    internal static IEnumerable<(string, Action)> Cases =>
    [
        ("alertas com a mesma condição preservam ações e cooldown independentes", Alerts),
        ("série que reaparece descarta cadência e suavização antigas", Tuning),
        ("configuração filtra gráficos/templates nulos antes de normalizar", Config),
        ("configuração portátil usa diretório próprio e preserva instalações", ConfigLocations),
        ("CSV abre segmento com novas séries e preserva registros anteriores", Csv),
        ("fila de escrita permanece limitada quando o consumidor bloqueia", WriterQueue),
        ("rede Linux conta bytes sem multiplicar intervalos ociosos", Network),
        ("discos Linux são selecionados por sysfs, incluindo virtio e eMMC", Disks),
        ("autostart resolve AppImage persistente e configuração XDG", Startup),
        ("ring Vulkan preserva frames e rejeita leitura parcial", VulkanRing),
        ("FPS Linux troca processo ocioso e reinicia contadores por sessão", FpsSessions),
        ("extração ZIP preserva subpastas e rejeita traversal", Zip),
        ("extração tar Linux suporta espaços e apóstrofos sem shell", Tar),
        ("download rejeita SHA-256 ausente, inválido ou divergente", Digest),
        ("atalho Windows registra na thread proprietária", Hotkeys),
        ("updater Windows instala subpastas e restaura todos os arquivos após falha", UpdateTransaction)
    ];

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void ConfigLocations()
    {
        using var directory = new TestDirectory();
        var root = directory.Path;
        var repo = System.IO.Path.Combine(root, "repo");
        var output = System.IO.Path.Combine(repo, "build", "portable");
        var user = System.IO.Path.Combine(root, "user");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(user);
        File.WriteAllText(System.IO.Path.Combine(repo, "PhantomMonitor.slnx"), "");
        var userConfig = System.IO.Path.Combine(user, "config.json");
        File.WriteAllText(userConfig, "{\"theme\":\"neptune\"}");
        ConfigurationLocation Resolve(string folder, string? appImage = null, bool windows = true,
            string? legacy = null, bool registered = false) =>
            ConfigPaths.ResolveFor(folder, user, appImage, windows, legacy, registered);
        Check(Resolve(output).ConfigFile == System.IO.Path.Combine(repo, "config.json"), "Desenvolvimento deve usar raiz do repositório.");
        File.WriteAllText(System.IO.Path.Combine(output, ConfigPaths.PortableMarker), "portable");
        var portable = Resolve(output);
        var localConfig = System.IO.Path.Combine(output, "config.json");
        Check(portable.Kind == ConfigurationLocationKind.Portable && portable.ConfigFile == localConfig,
            "Marcador portátil deve prevalecer sobre raiz de desenvolvimento.");
        File.WriteAllText(System.IO.Path.Combine(output, "config.example.json"), "{\"theme\":\"slate\"}");
        var config = AppConfig.Load(localConfig, out var error, createIfMissing: true, exampleDirectory: output);
        Check(error is null && config.Theme == "slate" && File.Exists(localConfig), "Primeiro uso deve criar config local pelo exemplo.");
        Check(File.ReadAllText(userConfig).Contains("neptune"), "Configuração antiga por usuário não pode ser alterada.");
        config.Theme = "neptune";
        Check(config.Save(localConfig), "Configuração portátil deve ser gravável.");
        File.WriteAllText(System.IO.Path.Combine(output, "config.example.json"), "{\"theme\":\"slate\"}");
        Check(AppConfig.Load(localConfig, out error, true, output).Theme == "neptune", "Arquivo local existente não pode ser substituído.");
        Check(ConfigPaths.CanWritePortableConfig(localConfig) && !ConfigPaths.CanWritePortableConfig(localConfig,
            _ => throw new UnauthorizedAccessException()), "Falha de permissão deve ser detectada sem trocar de diretório.");
        var obstacle = System.IO.Path.Combine(root, "file");
        File.WriteAllText(obstacle, "x");
        AppConfig.Load(System.IO.Path.Combine(obstacle, "config.json"), out error, true, output);
        Check(error is not null && File.ReadAllText(userConfig).Contains("neptune"), "Falha ao criar config não deve usar config por usuário.");
        Check(Resolve(output, legacy: output).ConfigFile == userConfig, "Instalação antiga deve vencer marcador portátil de atualização.");
        Check(Resolve(output, registered: true).ConfigFile == userConfig, "Instalação antiga registrada deve continuar por usuário.");
        File.WriteAllText(System.IO.Path.Combine(output, ConfigPaths.InstalledMarker), "installed");
        Check(Resolve(output).Kind == ConfigurationLocationKind.Installed && Resolve(output).ConfigFile == userConfig,
            "Marcador instalado deve vencer marcador portátil.");
        var image = System.IO.Path.Combine(root, "Phantom Monitor.AppImage");
        File.WriteAllText(image, "image");
        var appImage = Resolve(output, image, windows: false);
        Check(appImage.Kind == ConfigurationLocationKind.Portable && appImage.ConfigFile == System.IO.Path.Combine(root, "config.json"),
            "AppImage deve usar a pasta do arquivo externo, não a montagem interna.");
        File.Delete(image);
        Check(Resolve(output, image, windows: false).ConfigFile == System.IO.Path.Combine(root, "config.json"),
            "AppImage não deve mudar silenciosamente de pasta quando o arquivo externo estiver inacessível.");
        Check(Resolve(output, windows: false, legacy: output).ConfigFile == userConfig, "Pacote Linux instalado deve usar config por usuário.");
    }

    private static void Alerts()
    {
        var config = new AlertsConfig { Rules = [new() { Threshold = 80, Popup = true }, new() { Threshold = 80, Script = "script" }] };
        var evaluator = new AlertRuleEvaluator(config);
        var data = new SensorData(); data.SetValue(SensorType.CpuTemperature, 90);
        Check(evaluator.Evaluate(data).Count == 2, "Uma regra suprimiu a outra.");
        Check(evaluator.Evaluate(data).Count == 0, "Cooldown não foi preservado.");
    }

    private static void Tuning()
    {
        var now = DateTime.UtcNow;
        var tuning = new SensorTuningEngine([new() { Series = "CpuTemperature", IntervalMs = 10000, Smoothing = 3 }], () => now);
        var data = new SensorData(); data.SetValue(SensorType.CpuTemperature, 30); tuning.Apply(data);
        now = now.AddSeconds(1); tuning.Apply(new SensorData());
        now = now.AddSeconds(1); data = new(); data.SetValue(SensorType.CpuTemperature, 90); tuning.Apply(data);
        Check(data.GetValue(SensorType.CpuTemperature) == 90, "Série nova recebeu estado anterior.");
    }

    private static void Config()
    {
        var config = new AppConfig { Theme = "slate", UpdateIntervalMs = 500 };
        config.Overlay.Graphs.Add(null!); config.Overlay.Templates.Add(null!);
        config.Normalize();
        Check(config.Theme == "slate" && config.UpdateIntervalMs == 500, "Opções independentes foram alteradas.");
        Check(config.Overlay.Graphs.Count == 0 && config.Overlay.Templates.Count == 0, "Nulos não removidos.");

        var oldConfig = JsonSerializer.Deserialize<AppConfig>("{}")!;
        oldConfig.Normalize();
        Check(oldConfig.NavigationPosition == "left", "Configuração antiga deve manter navegação lateral.");
        var bottomConfig = JsonSerializer.Deserialize<AppConfig>("{\"navigationPosition\":\"bottom\"}")!;
        bottomConfig.Normalize();
        Check(bottomConfig.NavigationPosition == "bottom", "Configuração deve preservar a navegação inferior.");
        using var directory = new TestDirectory();
        var configPath = Path.Combine(directory.Path, "config.json");
        Check(bottomConfig.Save(configPath), "Configuração com navegação inferior deve ser gravada.");
        var reloadedConfig = AppConfig.Load(configPath, out var loadError);
        Check(loadError is null && reloadedConfig.NavigationPosition == "bottom", "Navegação inferior deve persistir após recarregar a configuração.");
        var invalidNavigation = JsonSerializer.Deserialize<AppConfig>("{\"navigationPosition\":\"unknown\"}")!;
        invalidNavigation.Normalize();
        Check(invalidNavigation.NavigationPosition == "left", "Posição inválida deve voltar à navegação lateral.");
    }

    private static void Csv()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "capture.csv");
        using (var logger = new CsvLogger(path))
        {
            var data = new SensorData(); data.SetValue(SensorType.CpuTotal, 10); logger.Write(data);
            data = new(); data.SetValue(SensorType.CpuTotal, 20); data.SetValue(SensorType.Fps, 60); logger.Write(data);
            data = new(); data.SetValue(SensorType.CpuTotal, 30); logger.Write(data);
        }
        Check(File.ReadAllLines(path).Length == 2, "Primeiro segmento foi sobrescrito.");
        var part = File.ReadAllLines(Path.Combine(directory.Path, "capture.part002.csv"));
        Check(part[0].Contains("Fps") && part.Length == 3 && part[^1].EndsWith(','), "Esquema/célula ausente incorretos.");
    }

    private static void Network()
    {
        using var directory = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "net"));
        var now = DateTime.UtcNow;
        var reader = new LinuxDiskNetworkReader(directory.Path, directory.Path, () => now);
        void Write(long bytes) => File.WriteAllText(Path.Combine(directory.Path, "net", "dev"), $"eth0: {bytes} 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\n");
        Write(0); reader.ReadNetworkStats(new());
        now = now.AddSeconds(1); Write(100); reader.ReadNetworkStats(new());
        now = now.AddSeconds(10); reader.ReadNetworkStats(new());
        now = now.AddSeconds(1); Write(200); var data = new SensorData(); reader.ReadNetworkStats(data);
        Check(data.GetValue(SensorType.NetworkDownTotal) == 200 && data.GetValue(SensorType.NetworkDown) == 100, "Bytes inflados após inatividade.");
    }

    private static void WriterQueue()
    {
        using var gate = new ManualResetEventSlim(false);
        using var writer = new BackgroundSnapshotWriter();
        writer.AddWriter(_ => gate.Wait());
        try
        {
            for (var i = 0; i < 1000; i++) writer.Write(new SensorData());
            Check(writer.DroppedSnapshots > 0, "Fila cresceu sem limite com consumidor bloqueado.");
        }
        finally { gate.Set(); }
    }

    private static void Disks()
    {
        using var directory = new TestDirectory();
        var reader = new LinuxDiskNetworkReader(directory.Path, directory.Path);
        foreach (var name in new[] { "vda", "xvda", "mmcblk0", "sdaa", "nvme0n1", "loop0", "dm-0" }) Directory.CreateDirectory(Path.Combine(directory.Path, name));
        foreach (var name in new[] { "vda", "xvda", "mmcblk0", "sdaa", "nvme0n1" }) Check(reader.IsPhysicalDisk(name), name);
        Check(!reader.IsPhysicalDisk("vda1") && !reader.IsPhysicalDisk("loop0") && !reader.IsPhysicalDisk("dm-0"), "Partição/agregador duplicado.");
    }

    private static void Startup()
    {
        using var directory = new TestDirectory();
        var image = Path.Combine(directory.Path, "Monitor's $100%.AppImage"); File.WriteAllText(image, "test");
        Check(LinuxStartupManager.ResolveExecutable(image, "/tmp/.mount/app") == image, "Caminho temporário escolhido.");
        Check(LinuxStartupManager.ResolveConfigRoot(directory.Path, "home") == directory.Path, "XDG ignorado.");
        Check(LinuxStartupManager.FormatExec(image).Contains("%%"), "Percentual de Exec não foi escapado.");
    }

    private static VulkanTelemetrySnapshot Snapshot(uint pid, ulong session, ulong time, params float[] frametimes)
        => new(pid, $"game{pid}", session, (ulong)frametimes.Length, time,
            frametimes.Select((ft, index) => new VulkanFrame((ulong)index + 1, time, ft, 1000 / ft)).ToArray(), false);

    private static void VulkanRing()
    {
        var bytes = new byte[LinuxVulkanTelemetryReader.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x50484E54); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 10); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(20), 1000); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(100), 4096);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(104), 6); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(112), 1);
        for (var i = 0; i < 3; i++)
        {
            var slot = bytes.AsSpan(120 + i * 24, 24);
            BinaryPrimitives.WriteUInt64LittleEndian(slot, (ulong)i + 1); BinaryPrimitives.WriteUInt64LittleEndian(slot[8..], 1000);
            BinaryPrimitives.WriteSingleLittleEndian(slot[16..], i == 1 ? 100 : 10); BinaryPrimitives.WriteSingleLittleEndian(slot[20..], i == 1 ? 10 : 100);
        }
        var parsed = LinuxVulkanTelemetryReader.Parse(bytes, 6);
        Check(parsed?.Frames.Count == 3 && parsed.Frames[1].FrametimeMs == 100, "Spike entre polls foi perdido.");
        Check(LinuxVulkanTelemetryReader.Parse(bytes, 8) is null && LinuxVulkanTelemetryReader.Parse(bytes.AsSpan(0, 100), 6) is null, "Leitura parcial aceita.");
    }

    private static void FpsSessions()
    {
        using var monitor = new LinuxFpsMonitor();
        monitor.Consume([Snapshot(1, 1, 1_000_000_000, 10, 100, 10)], 1_000_000_000);
        Check(monitor.FrametimeHistory.Count == 3, "Não consumiu todos os frames.");
        monitor.Consume([Snapshot(1, 1, 1_000_000_000, 10), Snapshot(2, 1, 5_000_000_000, 20)], 5_000_000_000);
        Check(monitor.HasData && monitor.FrametimeHistory.Count == 1 && monitor.FrametimeMs == 20, "Processo ocioso bloqueou o novo.");
        monitor.Consume([Snapshot(2, 2, 5_000_000_010, 30)], 5_000_000_010);
        Check(monitor.FrametimeMs == 30 && monitor.FrametimeHistory.Count == 1, "Sessão não reiniciou.");
        monitor.Consume([], 10_000_000_000);
        Check(!monitor.HasData && monitor.FpsAvg == 0, "Métricas expiradas permaneceram disponíveis.");
    }

    private static void Zip()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "update.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("PhantomOverlay/x64/hook.dll").Open())) writer.Write("native");
        var target = Path.Combine(directory.Path, "payload"); Directory.CreateDirectory(target);
        WindowsUpdateApplier.ExtractZipAsync(path, target, default).GetAwaiter().GetResult();
        Check(File.ReadAllText(Path.Combine(target, "PhantomOverlay", "x64", "hook.dll")) == "native", "Subpasta perdida.");
        var bad = Path.Combine(directory.Path, "bad.zip");
        using (var archive = ZipFile.Open(bad, ZipArchiveMode.Create)) archive.CreateEntry("../outside.txt");
        try { WindowsUpdateApplier.ExtractZipAsync(bad, target, default).GetAwaiter().GetResult(); throw new InvalidOperationException("Traversal aceito."); }
        catch (IOException) { }
        var applier = new LinuxUpdateApplier();
        Check(applier.PayloadPath(directory.Path, "1.2.3").StartsWith(Path.Combine(directory.Path, "1.2.3")), "Payload fora da versão.");
    }

    private static void Hotkeys()
    {
        if (!OperatingSystem.IsWindows()) throw new TestSkippedException("Requer Windows.");
        using var hotkeys = new GlobalHotkeyService();
        Check(hotkeys.Register(901, "Ctrl+Alt+Shift+F24", () => {}), "RegisterHotKey falhou.");
        hotkeys.UnregisterAll();
        Check(hotkeys.Register(901, "Ctrl+Alt+Shift+F24", () => {}), "Remoção/re-registro falhou.");
    }

    private static void Digest()
    {
        var hash = System.Security.Cryptography.SHA256.HashData("payload"u8);
        GitHubReleaseSource.ValidateDigest("sha256:" + Convert.ToHexString(hash), hash);
        foreach (var invalid in new string?[] { null, "sha256:bad", "sha512:bad", "sha256:" + new string('0', 64) })
        {
            var rejected = false;
            try { GitHubReleaseSource.ValidateDigest(invalid, hash); } catch (IOException) { rejected = true; }
            Check(rejected, "Digest inválido foi aceito.");
        }
    }

    private static void Tar()
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source's data");
        var target = Path.Combine(directory.Path, "target's data");
        Directory.CreateDirectory(Path.Combine(source, "native")); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "native", "layer.so"), "native");
        var archive = Path.Combine(directory.Path, "release's data.tar.gz");
        using (var output = File.Create(archive))
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
            System.Formats.Tar.TarFile.CreateFromDirectory(source, gzip, includeBaseDirectory: false);
        LinuxUpdateApplier.ExtractPortableAsync(archive, target, default).GetAwaiter().GetResult();
        Check(File.ReadAllText(Path.Combine(target, "native", "layer.so")) == "native", "Tar falhou com caminhos especiais.");
    }

    private static void UpdateTransaction()
    {
        if (!OperatingSystem.IsWindows()) throw new TestSkippedException("Script Windows; Linux possui validação separada.");
        using var directory = new TestDirectory();
        var updater = Path.Combine(directory.Path, "updater.ps1"); File.WriteAllText(updater, UpdatePayload.ReadScript("windows.ps1"));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(AppContext.BaseDirectory, "UpdateTransaction.Tests.ps1"), "-Root", directory.Path, "-UpdaterScript", updater }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        Check(process.WaitForExit(30000) && process.ExitCode == 0, "Transação/rollback falhou.");
    }
}

internal sealed class TestDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "phantom-regression-" + Guid.NewGuid().ToString("N"));
    internal TestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
