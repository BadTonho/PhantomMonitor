using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

public sealed partial class AppConfig
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

    /// <summary>
    /// Tema visual builtin (F1.6/Fase 3 do sistema de temas): <c>"neptune"</c>
    /// (visual original do Avalonia) ou <c>"slate"</c> (porta verbatim do visual
    /// do WPF). Aplicado no boot — a CLI <c>--theme=&lt;nome&gt;</c> sobrepõe o
    /// valor do arquivo (smoke/troubleshooting) e <c>--ui-theme=</c> segue aceito
    /// como legado — e, desde a Fase 3, trocável em runtime pelo seletor na
    /// página Configurações (App.ApplyTheme, sem reiniciar).
    /// </summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "neptune";

    /// <summary>Posição da navegação principal: <c>left</c>, <c>bottom</c> ou <c>dock</c>.</summary>
    [JsonPropertyName("navigationPosition")]
    public string NavigationPosition { get; set; } = "left";

    /// <summary>Exibição da navegação principal: <c>iconsAndText</c> ou <c>iconsOnly</c>.</summary>
    [JsonPropertyName("navigationDisplayMode")]
    public string NavigationDisplayMode { get; set; } = "iconsAndText";

    /// <summary>Personalizações opcionais, separadas por tema builtin.</summary>
    [JsonPropertyName("themePalettes")]
    public Dictionary<string, ThemePaletteConfig> ThemePalettes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Builds da era F1.6 serializavam o tema como `uiTheme`. O campo é lido nessa
    // grafia por compatibilidade, mas o texto documentado e escrito é `theme`
    // acima. Regra de migração: o legado só vale enquanto o canônico estiver no
    // valor default (`neptune`) — configs que só tinham `uiTheme` migram de valor.
    [JsonPropertyName("uiTheme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyUiTheme
    {
        get => null;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (Theme == "neptune") Theme = value;
        }
    }

    public double OverlayOpacity { get; set; } = 0.85;
    public OverlayPositionConfig OverlayPosition { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public SensorConfig Sensors { get; set; } = new();
    public AlertsConfig Alerts { get; set; } = new();

    /// <summary>Exportador HTTP do SDK de telemetria (opt-in; ver TelemetryExportConfig).</summary>
    public TelemetryExportConfig TelemetryExport { get; set; } = new();

    /// <summary>Verificação de atualizações via GitHub Releases (ver UpdateService, Services.Update).</summary>
    public UpdateConfig Update { get; set; } = new();

    /// <summary>Preferências de visibilidade dos atalhos beta na barra lateral.</summary>
    [JsonPropertyName("beta")]
    public BetaConfig Beta { get; set; } = new();

    /// <summary>Sensores customizados (fórmulas sobre outras séries; ver CustomSensorEngine).</summary>
    public List<CustomSensorConfig> CustomSensors { get; set; } = [];

    /// <summary>Cadência de amostragem e suavização por série (P9.1; ver SensorTuningEngine).</summary>
    [JsonPropertyName("sensorTuning")]
    public List<SensorTuningConfig> SensorTunings { get; set; } = [];

    // Builds predating the Avalonia editor serialized this property using the
    // CLR name in camel case (`sensorTunings`). Read that spelling for
    // compatibility, but write the documented canonical key above.
    [JsonPropertyName("sensorTunings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SensorTuningConfig>? LegacySensorTunings
    {
        get => null;
        set
        {
            if (value is not null && (SensorTunings?.Count ?? 0) == 0)
                SensorTunings = value;
        }
    }

    /// <summary>Perfis por aplicativo do OSD (aplicam overrides quando o executável em foco casa).</summary>
    public List<OverlayProfile> Profiles { get; set; } = [];

    public static AppConfig Load(string path) => Load(path, out _);

    public static AppConfig Load(string path, out string? error, bool createIfMissing = false, string? exampleDirectory = null)
    {
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path) ?? "";
                var examplePath = new[]
                {
                    Path.Combine(dir, "config.example.json"),
                    Path.Combine(exampleDirectory ?? AppContext.BaseDirectory, "config.example.json")
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
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, LoadOptions) ?? new AppConfig();
                cfg.Normalize();
                return cfg;
            }

            var defaults = new AppConfig();
            if (createIfMissing && !defaults.Save(path))
                error = "Não foi possível criar o arquivo de configuração.";
            return defaults;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Config.Load", ex);
            error = "Não foi possível ler ou criar o arquivo de configuração.";
            return new AppConfig();
        }
    }

    // Options cacheados: recriar por chamada invalide o cache interno de metadados
    // de reflexão do System.Text.Json e era chamado a cada Save (UI thread).
    private static readonly JsonSerializerOptions LoadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public bool Save(string path)
    {
        try
        {
            var json = JsonSerializer.Serialize(this, SaveOptions);

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
