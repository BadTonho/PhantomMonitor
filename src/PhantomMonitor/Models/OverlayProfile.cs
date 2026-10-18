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

    public string? AccentColor { get; set; }
}

/// <summary>Estado compartilhado do matcher: perfis vigentes + perfil fixado pela bandeja.</summary>
public sealed class OverlayProfileState
{
    public IReadOnlyList<OverlayProfile> Profiles { get; init; } = [];
    public string? Pinned { get; init; }
}
