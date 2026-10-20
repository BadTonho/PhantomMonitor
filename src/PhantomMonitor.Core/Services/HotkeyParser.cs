namespace PhantomMonitor.Services;

/// <summary>
/// Converte atalhos textuais ("Ctrl+Shift+O", "F10", "Alt+Shift+F12") em
/// modificadores e virtual-key codes do Win32 aceitos por RegisterHotKey.
/// </summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>
    /// Aceita apenas combinações seguras como atalho global: com pelo menos um modificador
    /// (Ctrl/Alt/Shift/Win) ou uma tecla de função (F1–F24) — evita capturar teclas soltas
    /// como "A" ou "5" para o usuário.
    /// </summary>
    public static bool TryParse(string? spec, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var parts = spec.Split(['+', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModWin;
                    break;
                default:
                    if (!TryParseKey(part, out virtualKey)) return false;
                    break;
            }
        }

        bool isFunctionKey = virtualKey is >= 0x70 and <= 0x87;
        return virtualKey != 0 && (modifiers != 0 || isFunctionKey);
    }

    private static bool TryParseKey(string token, out uint virtualKey)
    {
        virtualKey = 0;

        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }
            return false;
        }

        if ((token[0] == 'F' || token[0] == 'f') &&
            int.TryParse(token.AsSpan(1), out int fn) && fn is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + fn - 1);
            return true;
        }

        return false;
    }
}
