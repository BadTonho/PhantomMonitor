using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using PhantomMonitor.Models;

namespace PhantomMonitor.App.Services;

/// <summary>Resolve os ajustes da paleta sobre as cores builtin do tema selecionado.</summary>
internal static class ThemePaletteResolver
{
    public static ResourceDictionary LoadThemeDictionary(string theme)
    {
        var source = theme == "slate"
            ? "avares://PhantomMonitor/Themes/Tokens.Slate.axaml"
            : "avares://PhantomMonitor/Themes/Tokens.Neptune.axaml";
        return (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(source));
    }

    public static IReadOnlyDictionary<string, Color> ReadDefaultColors(ResourceDictionary dictionary)
    {
        var colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in ThemePaletteCatalog.TokenKeys)
        {
            if (dictionary[token] is SolidColorBrush brush)
                colors[token] = brush.Color;
        }
        return colors;
    }

    public static IReadOnlyDictionary<string, Color> Resolve(
        ThemePaletteConfig? palette,
        IReadOnlyDictionary<string, Color> defaultColors)
    {
        var resolved = new Dictionary<string, Color>(defaultColors, StringComparer.OrdinalIgnoreCase);
        if (palette is null) return resolved;

        foreach (var group in ThemePaletteCatalog.Groups)
        {
            if (!palette.BaseColors.TryGetValue(group.Key, out var baseHex)
                || !defaultColors.TryGetValue(group.AnchorToken, out var defaultAnchor))
                continue;

            var selectedBase = Color.Parse(baseHex);
            var anchorHsl = ToHsl(defaultAnchor);
            var selectedHsl = ToHsl(selectedBase);
            var hueDelta = selectedHsl.H - anchorHsl.H;
            var saturationDelta = selectedHsl.S - anchorHsl.S;
            var lightnessDelta = selectedHsl.L - anchorHsl.L;

            foreach (var token in group.Tokens)
            {
                if (!defaultColors.TryGetValue(token, out var defaultColor)) continue;
                var hsl = ToHsl(defaultColor);
                resolved[token] = FromHsl(
                    hsl.H + hueDelta,
                    Math.Clamp(hsl.S + saturationDelta, 0, 1),
                    Math.Clamp(hsl.L + lightnessDelta, 0, 1),
                    defaultColor.A);
            }
        }

        foreach (var (token, colorHex) in palette.TokenOverrides)
        {
            if (defaultColors.ContainsKey(token))
                resolved[token] = Color.Parse(colorHex);
        }

        return resolved;
    }

    public static string ToHex(Color color) => color.A == 255
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private readonly record struct Hsl(double H, double S, double L);

    private static Hsl ToHsl(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var lightness = (max + min) / 2;
        if (delta < 0.000001) return new Hsl(0, 0, lightness);

        var saturation = delta / (1 - Math.Abs(2 * lightness - 1));
        var hue = max switch
        {
            var value when Math.Abs(value - r) < 0.000001 => 60 * (((g - b) / delta) % 6),
            var value when Math.Abs(value - g) < 0.000001 => 60 * (((b - r) / delta) + 2),
            _ => 60 * (((r - g) / delta) + 4)
        };
        if (hue < 0) hue += 360;
        return new Hsl(hue, saturation, lightness);
    }

    private static Color FromHsl(double hue, double saturation, double lightness, byte alpha)
    {
        hue = ((hue % 360) + 360) % 360;
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var secondary = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        var match = lightness - chroma / 2;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, secondary, 0d),
            < 120 => (secondary, chroma, 0d),
            < 180 => (0d, chroma, secondary),
            < 240 => (0d, secondary, chroma),
            < 300 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };

        return Color.FromArgb(alpha,
            ToByte((r + match) * 255),
            ToByte((g + match) * 255),
            ToByte((b + match) * 255));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
