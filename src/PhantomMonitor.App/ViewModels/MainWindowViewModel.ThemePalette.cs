using PhantomMonitor.App.Services;
using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Dictionary<string, ThemePaletteConfig> _themePalettesDraft = new(StringComparer.OrdinalIgnoreCase);

    public string SelectedThemeKey => ThemeIndex == 1 ? "slate" : "neptune";

    public ThemePaletteEditorViewModel CreateThemePaletteEditor()
        => new(SelectedThemeKey, GetThemePaletteDraft(SelectedThemeKey));

    public ThemePaletteConfig GetThemePaletteDraft(string theme)
        => _themePalettesDraft.TryGetValue(theme, out var palette) ? palette.Clone() : new ThemePaletteConfig();

    public void SetThemePaletteDraft(string theme, ThemePaletteConfig palette)
    {
        var normalizedTheme = theme == "slate" ? "slate" : "neptune";
        if (palette.BaseColors.Count == 0 && palette.TokenOverrides.Count == 0)
            _themePalettesDraft.Remove(normalizedTheme);
        else
            _themePalettesDraft[normalizedTheme] = palette.Clone();
    }

    internal bool ThemePalettesDraftChanged => !ThemePalettesEqual(_themePalettesDraft, _config.ThemePalettes);

    internal Dictionary<string, ThemePaletteConfig> CloneThemePalettesDraft()
        => CloneThemePalettes(_themePalettesDraft);

    internal static Dictionary<string, ThemePaletteConfig> CloneThemePalettes(
        IReadOnlyDictionary<string, ThemePaletteConfig>? palettes)
        => new((palettes ?? new Dictionary<string, ThemePaletteConfig>())
            .ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    internal static bool ThemePalettesEqual(
        IReadOnlyDictionary<string, ThemePaletteConfig>? left,
        IReadOnlyDictionary<string, ThemePaletteConfig>? right)
    {
        left ??= new Dictionary<string, ThemePaletteConfig>();
        right ??= new Dictionary<string, ThemePaletteConfig>();
        if (left.Count != right.Count) return false;

        foreach (var (theme, palette) in left)
        {
            if (!right.TryGetValue(theme, out var other)
                || !ColorMapsEqual(palette.BaseColors, other.BaseColors)
                || !ColorMapsEqual(palette.TokenOverrides, other.TokenOverrides))
                return false;
        }
        return true;
    }

    private static bool ColorMapsEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count
            && left.All(entry => right.TryGetValue(entry.Key, out var value)
                && string.Equals(entry.Value, value, StringComparison.OrdinalIgnoreCase));
}
