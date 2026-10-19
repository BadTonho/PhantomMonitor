using System.Text.Json.Serialization;

namespace PhantomMonitor.Models;

/// <summary>
/// Perfil por aplicativo do OSD: aplica ajustes quando o executável em foco casa com
/// <see cref="AppMatch"/> (ou quando o usuário fixa o perfil pela bandeja). Só preencha
/// os campos que devem sobrescrever o config global — nulo/ausente = usa o global.
/// </summary>
public sealed class OverlayProfile
{
    public string Name { get; set; } = "";

    /// <summary>
    /// Nome do executável em foco (sem .exe, sem caminho, case-insensitive, ex.: "cs2").
    /// "" ou "*" casa com qualquer aplicativo (perfil "universal").
    /// </summary>
    public string AppMatch { get; set; } = "";

    public double? OffsetX { get; set; }
    public double? OffsetY { get; set; }

    public int? Width { get; set; }
    public double? FontSize { get; set; }

    /// <summary>Opacidade do painel (vazio = usa overlayOpacity global).</summary>
    public double? Opacity { get; set; }

    /// <summary>
    /// Whitelist de séries por instância deste perfil (padrões wildcards separados no campo
    /// "series" em modo vírgula: "Gpu*,FanSpeed*"). Vazio = usa a <c>overlay.shownSeries</c> global.
    /// </summary>
    [JsonPropertyName("series")]
    public List<string>? ShownSeries { get; set; }

    public string? AccentColor { get; set; }

    /// <summary>
    /// false esconde o OSD por completo enquanto este perfil casa (ex.: jogo que
    /// combina mal com overlays). null/ausente = visível (comportamento padrão).
    /// </summary>
    public bool? Visible { get; set; }
}

/// <summary>Estado compartilhado do matcher: perfis vigentes + perfil fixado pela bandeja.</summary>
public sealed class OverlayProfileState
{
    public IReadOnlyList<OverlayProfile> Profiles { get; init; } = [];
    public string? Pinned { get; init; }
}
