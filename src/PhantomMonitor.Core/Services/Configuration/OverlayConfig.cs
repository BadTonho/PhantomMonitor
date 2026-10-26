using System.Text.Json.Serialization;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

public sealed class OverlayPositionConfig
{
    public double X { get; set; } = 20;
    public double Y { get; set; } = 80;
}

public sealed class OverlayConfig
{
    /// <summary>Modo global de apresentação do OSD. "floating" preserva a janela desktop.</summary>
    public string RenderMode { get; set; } = "floating";
    /// <summary>Alvos de inicialização manual no Linux; preservados em configs legadas e ignorados pelo hook global Windows.</summary>
    public List<OverlayLaunchTargetConfig> InGameTargets { get; set; } = [];
    public int Width { get; set; } = 220;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowGpuModel { get; set; } = false;
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
    public string? BackgroundColor { get; set; }
    public double BackgroundOpacity { get; set; } = 1.0;
    public string FontFamily { get; set; } = "";
    public bool BorderEnabled { get; set; } = true;
    public string? BorderColor { get; set; }

    /// <summary>
    /// Posições normalizadas dos blocos do OSD em relação à área da janela
    /// acompanhada no Windows ou à área disponível no Linux. Valores fora de 0–1
    /// compensam offsets globais/de perfil; a coordenada visível é limitada à área.
    /// </summary>
    public Dictionary<string, OverlayBlockPosition> Layout { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Layout configurável do editor visual. Null indica config legado ainda não migrado.</summary>
    public List<OverlayBlockConfig>? Blocks { get; set; }
    [JsonIgnore]
    public bool BlocksMigrated { get; set; }

    /// <summary>Identidades de métricas de sensor ocultadas em todos os blocos do overlay.</summary>
    public List<string> HiddenMetrics { get; set; } = [];

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

public sealed class OverlayBlockPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class OverlayBlockConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double X { get; set; }
    public double Y { get; set; }
    public List<OverlayBlockItemConfig> Items { get; set; } = [];
    public List<OverlayMetricOverrideConfig> MetricOverrides { get; set; } = [];
    public List<OverlayCustomLineConfig> CustomLines { get; set; } = [];
    /// <summary>Optional metric row order per content rule, keyed by "kind:value".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<string>>? MetricOrder { get; set; }
}

public sealed class OverlayCustomLineConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public double? FontSize { get; set; }
    public double? Width { get; set; }
    public List<OverlayHardwareReference> Hardware { get; set; } = [];
}

public sealed class OverlayHardwareReference
{
    public string Token { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Category { get; set; } = "";
}

public sealed class OverlayBlockItemConfig
{
    public string Kind { get; set; } = "category";
    public string Value { get; set; } = "cpu";
}

public sealed class OverlayMetricOverrideConfig
{
    public string Identity { get; set; } = "";
    public string? DisplayName { get; set; }
    public int? Decimals { get; set; }
    public string? Color { get; set; }
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
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
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
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
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

public sealed class OverlayLaunchTargetConfig
{
    public string ExecutablePath { get; set; } = "";
    public string Arguments { get; set; } = "";
}
