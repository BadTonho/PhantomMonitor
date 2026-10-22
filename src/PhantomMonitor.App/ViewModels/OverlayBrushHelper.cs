using Avalonia.Media;

namespace PhantomMonitor.App.ViewModels;

/// <summary>
/// Tradução linha→brush do overlay: cores vêm do config do usuário
/// (<c>overlay.accentColor</c>, o <c>color</c> dos templates P6.6 e dos gráficos
/// P8.5) — dados de configuração, não tokens de tema. Cache por hex evita criar
/// SolidColorBrush a cada aplicação (o WPF congelava os espelhos dessas escovas).
/// Hex inválido/ausente cai no fallback (#1E90FF do config já é normalizado pelo
/// Normalize do Core; parse defensivo aqui é só de última linha).
/// </summary>
internal static class OverlayBrushHelper
{
    private static readonly Dictionary<string, IBrush> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IBrush> TintCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Brush sólido a partir de hex (#RGB/#RRGGBB/#AARRGGBB); invalid → fallback.</summary>
    public static IBrush Resolve(string? hex, IBrush fallback)
    {
        if (TryParseColor(hex, out var color))
        {
            var key = color.ToString();
            if (!Cache.TryGetValue(key, out var brush))
            {
                brush = new SolidColorBrush(color);
                Cache[key] = brush;
            }
            return brush;
        }
        return fallback;
    }

    /// <summary>Contorno do painel na cor do acento com alpha 0x30 (igual ao WPF).</summary>
    public static IBrush Tint(Color accentColor)
    {
        var key = accentColor.ToString();
        if (!TintCache.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(0x30, accentColor.R, accentColor.G, accentColor.B));
            TintCache[key] = brush;
        }
        return brush;
    }

    public static bool TryParseColor(string? hex, out Color color)
    {
        var value = (hex ?? "").Trim();
        // Parse aceita os mesmos formatos que o ColorConverter do WPF usava aqui.
        if (value.Length is 4 or 7 or 9 && value[0] == '#' && Color.TryParse(value, out color))
            return true;
        color = default;
        return false;
    }
}
