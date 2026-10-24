using System.Globalization;

namespace PhantomMonitor.App.ViewModels;

internal static class OverlaySensorPrecisionFormatter
{
    public static string Format(string formattedValue, double rawValue, int decimals)
    {
        if (!double.IsFinite(rawValue) || formattedValue.Length == 0)
            return formattedValue;

        var prefixLength = GetNumericPrefixLength(formattedValue);
        if (prefixLength == 0
            || !double.TryParse(formattedValue[..prefixLength], NumberStyles.Float,
                CultureInfo.CurrentCulture, out var displayedValue))
            return formattedValue;

        var suffix = formattedValue[prefixLength..];
        return displayedValue.ToString($"F{Math.Clamp(decimals, 0, 3)}", CultureInfo.CurrentCulture) + suffix;
    }

    private static int GetNumericPrefixLength(string value)
    {
        var index = 0;
        if (value[index] is '+' or '-') index++;
        var digits = 0;
        while (index < value.Length && char.IsDigit(value[index]))
        {
            index++;
            digits++;
        }

        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (digits > 0 && index + separator.Length <= value.Length
            && value.AsSpan(index, separator.Length).SequenceEqual(separator))
        {
            index += separator.Length;
            while (index < value.Length && char.IsDigit(value[index])) index++;
        }

        return digits > 0 ? index : 0;
    }
}
