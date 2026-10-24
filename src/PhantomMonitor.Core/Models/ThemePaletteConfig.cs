namespace PhantomMonitor.Models;

/// <summary>Personalizações de cor sobre um dos temas builtin do app.</summary>
public sealed class ThemePaletteConfig
{
    /// <summary>Cores-base por família visual. Os tons dos recursos são derivados dessas âncoras.</summary>
    public Dictionary<string, string> BaseColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Substituições exatas por chave de recurso; prevalecem sobre a cor-base da família.</summary>
    public Dictionary<string, string> TokenOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ThemePaletteConfig Clone() => new()
    {
        BaseColors = new Dictionary<string, string>(BaseColors ?? [], StringComparer.OrdinalIgnoreCase),
        TokenOverrides = new Dictionary<string, string>(TokenOverrides ?? [], StringComparer.OrdinalIgnoreCase)
    };
}

/// <summary>
/// Catálogo estável das famílias e recursos de cor presentes nos dicionários builtin.
/// Mantido na Core para que normalização e editor usem a mesma whitelist.
/// </summary>
public static class ThemePaletteCatalog
{
    public sealed record Group(string Key, string Name, string AnchorToken, IReadOnlyList<string> Tokens);

    public static IReadOnlyList<Group> Groups { get; } =
    [
        new("surfaces", "Superfícies", "BgCardBrush",
        [
            "BgWindowBrush", "BgSidebarBrush", "BgSidebarBorderBrush", "BgCardBrush", "BgCardBorderBrush",
            "InventoryValueCellBrush", "InventoryValueCellBorderBrush", "BgChipBrush", "BgDividerBrush",
            "BgOverlayPanelBrush", "BgOverlayButtonBrush", "AlertToastBackgroundBrush"
        ]),
        new("text", "Texto", "TextPrimaryBrush",
        [
            "TextPrimaryBrush", "TextBrightBrush", "TextBodyBrush", "TextStatusBrush", "TextMonoBrush",
            "TextSecondaryBrush", "TextMutedBrush", "TextOverlaySecondaryBrush", "TextLabelBrush", "TextDimBrush"
        ]),
        new("accents", "Destaques e estados", "AccentLightBrush",
        [
            "AccentLightBrush", "AccentChipCpuBrush", "AccentPurpleChipBrush", "AccentPurpleSoftBrush",
            "AccentGreenBrush", "AccentGreenSoftBrush", "AccentWarningBrush", "AccentHotBrush",
            "AccentHotSoftBrush", "AccentHotBodyBrush", "BgToastUpdateBrush", "BgTintGreenBrush",
            "BorderTintGreenBrush", "BgTintWarmBrush", "BorderTintWarmBrush", "BgTintHotBrush",
            "BorderTintHotBrush", "AlertToastBorderBrush", "AlertToastHeaderBrush"
        ]),
        new("navigation", "Navegação", "NavSelectedBrush",
        ["NavBrush", "NavSelectedBrush", "NavSoftBrush", "NavForegroundBrush", "NavForegroundSelectedBrush"]),
        new("overlay", "Overlay", "OverlayBorderBrush",
        ["OverlayBorderBrush", "OverlayGuideBrush", "OverlayGridBrush"])
    ];

    public static IReadOnlySet<string> GroupKeys { get; } =
        new HashSet<string>(Groups.Select(group => group.Key), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> TokenKeys { get; } =
        new HashSet<string>(Groups.SelectMany(group => group.Tokens), StringComparer.OrdinalIgnoreCase);
}
