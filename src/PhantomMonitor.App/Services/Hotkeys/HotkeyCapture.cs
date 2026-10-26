using Avalonia.Input;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Services.Hotkeys;

internal static class HotkeyCapture
{
    public static bool TryFormat(Key key, KeyModifiers modifiers, out string shortcut)
    {
        shortcut = "";
        var value = (int)key;
        string? name = value >= (int)Key.A && value <= (int)Key.Z
            ? ((char)('A' + value - (int)Key.A)).ToString()
            : value >= (int)Key.D0 && value <= (int)Key.D9
                ? ((char)('0' + value - (int)Key.D0)).ToString()
                : value >= (int)Key.F1 && value <= (int)Key.F24
                    ? $"F{value - (int)Key.F1 + 1}"
                    : null;
        if (name is null) return false;
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(name);
        var candidate = string.Join('+', parts);
        if (!HotkeyParser.TryParse(candidate, out _, out _)) return false;
        shortcut = candidate;
        return true;
    }

    public static bool SameCombination(string first, string second) =>
        HotkeyParser.TryParse(first, out var firstModifiers, out var firstKey)
        && HotkeyParser.TryParse(second, out var secondModifiers, out var secondKey)
        && firstModifiers == secondModifiers && firstKey == secondKey;
}
