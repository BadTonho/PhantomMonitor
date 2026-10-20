using System.IO;
using System.Text.Json;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

public sealed class AppConfig
{
    public int UpdateIntervalMs { get; set; } = 1000;
    public bool LogToCsv { get; set; } = false;
    public bool LogToJson { get; set; } = true;

    /// <summary>
    /// Journal JSON contínuo (JSONL): uma linha por amostra em
    /// <c>logs/sensors_*.jsonl</c>, com linhas "note" nos marcadores da sessão
    /// (início e resets de estatística) — replay de benchmarks sem reescrever
    /// o snapshot por inteiro. Ver JsonJournalLogger e docs/logging.md.
    /// </summary>
    public bool LogToJsonJournal { get; set; } = false;
    public bool ShowOverlay { get; set; } = true;
    public string ToggleOverlayHotkey { get; set; } = "";

    /// <summary>
    /// 2º atalho global (P6.7): pausa/retoma o monitoramento em runtime. Padrão
    /// Ctrl+Alt+P; vazio = desativado. O estado de pausa NÃO persiste no config —
    /// reiniciar o app retoma a coleta.
    /// </summary>
    public string PauseMonitorHotkey { get; set; } = "Ctrl+Alt+P";

    public double OverlayOpacity { get; set; } = 0.85;
    public OverlayPositionConfig OverlayPosition { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public SensorConfig Sensors { get; set; } = new();
    public AlertsConfig Alerts { get; set; } = new();

    /// <summary>Exportador HTTP do SDK de telemetria (opt-in; ver TelemetryExportConfig).</summary>
    public TelemetryExportConfig TelemetryExport { get; set; } = new();

    /// <summary>Sensores customizados (fórmulas sobre outras séries; ver CustomSensorEngine).</summary>
    public List<CustomSensorConfig> CustomSensors { get; set; } = [];

    /// <summary>Perfis por aplicativo do OSD (aplicam overrides quando o executável em foco casa).</summary>
    public List<OverlayProfile> Profiles { get; set; } = [];

    public static AppConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path) ?? "";
                var examplePath = new[]
                {
                    Path.Combine(dir, "config.example.json"),
                    Path.Combine(AppContext.BaseDirectory, "config.example.json")
                }.FirstOrDefault(File.Exists);

                if (examplePath != null)
                {
                    if (dir.Length > 0) Directory.CreateDirectory(dir);
                    File.Copy(examplePath, path);
                }
            }

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
                cfg.Normalize();
                return cfg;
            }

            return new AppConfig();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Config.Load", ex);
            return new AppConfig();
        }
    }

    private static readonly System.Text.RegularExpressions.Regex HexColorRegex =
        new("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Corrige valores inválidos vindos de um config editado à mão.</summary>
    public void Normalize()
    {
        if (UpdateIntervalMs < 100) UpdateIntervalMs = 100;
        OverlayOpacity = Math.Clamp(OverlayOpacity, 0.1, 1.0);
        OverlayPosition ??= new();
        Overlay ??= new();
        Sensors ??= new();

        // Exportador HTTP: porta em faixa de usuário (abaixo de 1024 costuma exigir
        // privilégio e confunde com serviços do sistema), loopback por padrão.
        TelemetryExport ??= new();
        TelemetryExport.Port = Math.Clamp(TelemetryExport.Port, 1024, 65535);

        // Whitelist de séries por instância: config antigo sem a chave (ou com lista vazia)
        // volta ao padrão, senão o OSD ficaria sem multi-GPU/ventoinhas/discos extras.
        string[] defaultSeries = ["Gpu*", "FanSpeed*", "Disk*"];
        if (Overlay.ShownSeries is not { Count: > 0 })
        {
            Overlay.ShownSeries = [.. defaultSeries];
        }
        else
        {
            Overlay.ShownSeries.RemoveAll(string.IsNullOrWhiteSpace);
            Overlay.ShownSeries = Overlay.ShownSeries
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (Overlay.ShownSeries.Count == 0) Overlay.ShownSeries = [.. defaultSeries];
        }

        Overlay.Width = Math.Clamp(Overlay.Width, 140, 700);
        Overlay.FontSize = Math.Clamp(Overlay.FontSize, 8, 20);
        if (!IsValidHexColor(Overlay.AccentColor)) Overlay.AccentColor = "#1E90FF";

        // Offset do overlay relativo à janela em foco: extremos digitados à mão no editor
        // deixavam o overlay nascendo fora da área de trabalho, invisível e irrecuperável.
        OverlayPosition.X = Math.Clamp(OverlayPosition.X, -5000, 5000);
        OverlayPosition.Y = Math.Clamp(OverlayPosition.Y, -5000, 5000);

        ToggleOverlayHotkey = (ToggleOverlayHotkey ?? "").Trim();
        PauseMonitorHotkey = (PauseMonitorHotkey ?? "").Trim();

        Alerts ??= new();
        Alerts.CooldownSeconds = Math.Clamp(Alerts.CooldownSeconds, 1, 3600);
        Alerts.Rules ??= new();
        Alerts.Rules.RemoveAll(rule => rule is null);
        foreach (var rule in Alerts.Rules)
        {
            rule.CooldownSeconds = Math.Clamp(rule.CooldownSeconds, 0, 3600);
            rule.Script ??= "";
            rule.ScriptArguments ??= "";
            rule.Message ??= "";
        }

        // Perfis por aplicativo: nomes obrigatórios, faixas dos overrides, AppMatch limpo.
        Profiles ??= new();
        Profiles.RemoveAll(p => p is null);
        foreach (var profile in Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                profile.Name = $"Perfil {Profiles.IndexOf(profile) + 1}";

            profile.AppMatch = (profile.AppMatch ?? "").Trim('.').Trim();
            if (profile.AppMatch.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                profile.AppMatch = profile.AppMatch[..^4].Trim();

            if (profile.OffsetX is { } x) profile.OffsetX = Math.Clamp(x, -5000, 5000);
            if (profile.OffsetY is { } y) profile.OffsetY = Math.Clamp(y, -5000, 5000);
            if (profile.Width is { } w) profile.Width = Math.Clamp(w, 140, 700);
            if (profile.FontSize is { } f) profile.FontSize = Math.Clamp(f, 8, 20);
            if (profile.Opacity is { } o) profile.Opacity = Math.Clamp(o, 0.1, 1.0);

            if (profile.ShownSeries is { Count: > 0 })
            {
                profile.ShownSeries.RemoveAll(string.IsNullOrWhiteSpace);
                profile.ShownSeries = profile.ShownSeries
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (profile.ShownSeries.Count == 0) profile.ShownSeries = null;
            }

            if (profile.AccentColor is { } c && !IsValidHexColor(c)) profile.AccentColor = null;
        }

        // Templates do OSD (P6.6): texto obrigatório, cor valida (hex) senão cai no
        // branco padrão; templates sem texto não renderizam nada — descartados aqui.
        Overlay.Templates ??= new();
        Overlay.Templates.RemoveAll(t => t is null);
        Overlay.Templates.RemoveAll(t => string.IsNullOrWhiteSpace(t.Text));
        foreach (var template in Overlay.Templates)
        {
            template.Text = template.Text.Trim();
            if (template.Color is { } color && !IsValidHexColor(color)) template.Color = null;
        }

        // Sensores customizados: nome obrigatório (removidos com fórmula que não
        // compilam — erro registrado, os válidos mantêm a ordem/índice do config).
        CustomSensors ??= [];
        CustomSensors.RemoveAll(s => s is null || string.IsNullOrWhiteSpace(s.Name));
        foreach (var sensor in CustomSensors)
        {
            sensor.Name = sensor.Name.Trim();
            sensor.Formula = (sensor.Formula ?? "").Trim();
            sensor.Unit = (sensor.Unit ?? "").Trim();

            if (!CustomSensorEngine.IsFormulaValid(sensor.Formula))
            {
                ErrorLog.Write("Config.Normalize",
                    $"customSensors: fórmula inválida descartada para '{sensor.Name}': '{sensor.Formula}'");
                sensor.Formula = "";
            }
        }
        CustomSensors.RemoveAll(s => string.IsNullOrWhiteSpace(s.Formula));
        if (CustomSensors.Count > CustomSensorEngine.MaxSensors)
        {
            CustomSensors.RemoveRange(CustomSensorEngine.MaxSensors,
                CustomSensors.Count - CustomSensorEngine.MaxSensors);
        }

        // Gráficos do OSD (P8.5): série obrigatória (descartada sem ela), altura e
        // janela clamped; eixo fixo com min>max troca os valores; cor valida (hex).
        Overlay.Graphs ??= new();
        Overlay.Graphs.RemoveAll(g => g is null || string.IsNullOrWhiteSpace(g.Series));
        foreach (var graph in Overlay.Graphs)
        {
            graph.Series = graph.Series!.Trim();
            graph.Height = Math.Clamp(graph.Height, 12, 120);
            graph.Window = (int)Math.Clamp(graph.Window, 30, 1200);
            if (graph.Color is { } color && !IsValidHexColor(color)) graph.Color = null;
            if (graph.Min is { } min && graph.Max is { } max && min > max) (graph.Min, graph.Max) = (graph.Max, graph.Min);
            if (graph.Min is { } m1 && !double.IsFinite(m1)) graph.Min = null;
            if (graph.Max is { } m2 && !double.IsFinite(m2)) graph.Max = null;
        }
        if (Overlay.Graphs.Count > 12) Overlay.Graphs.RemoveRange(12, Overlay.Graphs.Count - 12);
    }

    internal static bool IsValidHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HexColorRegex.IsMatch(value);

    public bool Save(string path)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(this, options);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Escrita atômica: evita config.json truncado se o app fechar no meio.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Config.Save", ex);
            return false;
        }
    }
}

public sealed class OverlayPositionConfig
{
    public double X { get; set; } = 20;
    public double Y { get; set; } = 80;
}

public sealed class OverlayConfig
{
    public int Width { get; set; } = 220;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowDisk { get; set; } = true;
    public bool ShowNetwork { get; set; } = false;
    public bool ShowFans { get; set; } = true;
    public bool ShowMotherboard { get; set; } = false;
    public bool ShowFps { get; set; } = true;
    public bool ShowFrametimeGraph { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    public string AccentColor { get; set; } = "#1E90FF";
    public double FontSize { get; set; } = 11.5;

    /// <summary>
    /// Linhas de template do usuário (P6.6): texto livre com placeholders
    /// {Chave} / {Chave:N} resolvidos no fim do painel; ver OverlayTemplate.
    /// </summary>
    public List<OverlayTemplate> Templates { get; set; } = [];

    /// <summary>
    /// Gráficos de histórico por série (P8.5): sparkline embutida no OSD com
    /// janela de amostras própria — resolve a série como as templates
    /// (chave, wildcard ou nome amigável). Ver OverlayGraph.
    /// </summary>
    public List<OverlayGraph> Graphs { get; set; } = [];

    /// <summary>
    /// Whitelist (padrões wildcard) das séries por instância exibidas no OSD além das
    /// linhas canônicas. Ex.: "Gpu*" (multi-GPU), "FanSpeed*" (ventoinhas extras),
    /// "Disk*" (rendimento/saúde por disco), "CpuTemperature#*" (temp por núcleo),
    /// "CpuPerCore#*", "CpuClock#*", "VrmTemperature*", "RailVoltage*", "Network*"
    /// (por interface + totais da sessão). "*" libera tudo. Ver <see cref="SensorKey.MatchesPattern"/>.
    /// </summary>
    public List<string>? ShownSeries { get; set; } = ["Gpu*", "FanSpeed*", "Disk*"];
}

/// <summary>
/// Linha personalizada do OSD (P6.6): <c>text</c> é renderizado no fim do painel
/// com placeholders resolvidos a cada ciclo — <c>{GpuMemoryTotal}</c> casa a série
/// canônica (ou a primeira instância quando ela não existir; <c>{GpuTemperature#1}</c>
/// é explícito; aceita wildcard <c>{Gpu*}</c> = primeiro match) e <c>{Key:N}</c>
/// fixa N decimais (0–3, padrão 1). A linha fica escondida enquanto qualquer
/// placeholder não resolver. Cor opcional apenas em config (#RRGGBB/#AARRGGBB).
/// </summary>
public sealed class OverlayTemplate
{
    public string Text { get; set; } = "";

    /// <summary>Cor da linha (#RRGGBB ou #AARRGGBB); null/válido ausente = branco padrão.</summary>
    public string? Color { get; set; }
}

/// <summary>
/// Gráfico de histórico de uma série no OSD (P8.5): sparkline azul-escura
/// embutida no painel — reusa o mesmo buffer circular do frametime. A série é
/// resolvida como nos placeholders de template (canônica → primeira instância,
/// wildcard "GpuTemperature#*", nome amigável "DeltaTemp"). Eixo Y automático
/// (janela atual) ou fixo com <c>min</c>/<c>max</c> opcionais; cor segue o
/// acento do OSD quando <c>color</c> fica vazio. Entrada só via config
/// (mesma política das templates): novas voltas do editor aplicam recriando o overlay.
/// </summary>
public sealed class OverlayGraph
{
    /// <summary>Série monitorada: "GpuTemperature", "GpuTemperature#1", "Custom#*" ou nome amigável.</summary>
    public string Series { get; set; } = "";

    /// <summary>Altura da sparkline em px (padrão 36).</summary>
    public double Height { get; set; } = 36;

    /// <summary>Amostras na janela circular (30–1200; padrão 240 ≈ 4 min em 1 Hz).</summary>
    public int Window { get; set; } = 240;

    /// <summary>Cor da linha (#RRGGBB/#AARRGGBB); null/vazio = cor de acento do OSD.</summary>
    public string? Color { get; set; }

    /// <summary>Piso do eixo Y (opcional; auto quando null).</summary>
    public double? Min { get; set; }

    /// <summary>Teto do eixo Y (opcional; auto quando null).</summary>
    public double? Max { get; set; }
}

public sealed class SensorConfig
{
    public bool Cpu { get; set; } = true;
    public bool Gpu { get; set; } = true;
    public bool Ram { get; set; } = true;
    public bool Disk { get; set; } = true;
    public bool Network { get; set; } = false;
    public bool Motherboard { get; set; } = false;
}

/// <summary>
/// Sensor customizado (paridade HWiNFO "custom user sensor"): fórmula derivada de
/// outras séries do snapshot, avaliada a cada ciclo pela <see cref="CustomSensorEngine"/>
/// e publicada como série <c>Custom#i</c> (i = posição da fórmula no config, ≥ 1).
/// <c>formula</c> segue a sintaxe do parser (operadores + - * /, parênteses, funções
/// min/max/abs e referências {"Chave"} — ver docs/funções/sensores-customizados.md).
/// <c>unit</c> é opcional e só compõe o rótulo exibido ("Média (°C)").
/// </summary>
public sealed class CustomSensorConfig
{
    /// <summary>Nome amigável (vira o rótulo da série e a referência {"Nome"} em fórmulas).</summary>
    public string Name { get; set; } = "";

    public string Formula { get; set; } = "";

    /// <summary>Unidade opcional exibida no rótulo e nas planilhas ("°C", "%", "MB"...).</summary>
    public string Unit { get; set; } = "";
}

/// <summary>
/// Exportador HTTP do SDK de telemetria (P6.3): publica o último snapshot como
/// JSON em <c>GET /values</c> (mesmo payload do sensors_latest.json) e
/// <c>GET /health</c>. Por padrão desligado e escutando no loopback
/// (127.0.0.1, sem exigir permissão de URL do http.sys); <c>listenAll</c>
/// abre para a LAN (<c>http://+:porta/</c>) e pode requerer liberação de
/// firewall. UDP não foi implementado de propósito: HTTP dá discovery
/// (health), Content-Length e cache headers sem nenhum parser no cliente —
/// justificativa completa em docs/telemetry-sdk.md.
/// </summary>
public sealed class TelemetryExportConfig
{
    public bool Enabled { get; set; }

    /// <summary>Porta TCP do listener (1024–65535; padrão 7317).</summary>
    public int Port { get; set; } = 7317;

    /// <summary>false = escuta só em 127.0.0.1; true = todas as interfaces (LAN, opt-in).</summary>
    public bool ListenAll { get; set; }
}

public sealed class AlertsConfig
{
    public bool Enabled { get; set; } = true;
    public int CooldownSeconds { get; set; } = 60;

    /// <summary>Trava de segurança: sem true, regras com a ação "shutdown" são ignoradas.</summary>
    public bool AllowShutdown { get; set; }

    public List<AlertRule> Rules { get; set; } = [];
}
