namespace PhantomMonitor.App.ViewModels;

/// <summary>Converts normalized overlay positions to and from a surface's usable area.</summary>
internal static class OverlayPositionMath
{
    public static double AvailableRange(double areaSize, double blockSize)
        => Math.Max(0, areaSize - blockSize);

    public static double ToNormalized(double position, double areaSize, double blockSize)
    {
        var range = AvailableRange(areaSize, blockSize);
        return range <= 0 || !double.IsFinite(position) ? 0 : Math.Clamp(position / range, 0, 1);
    }

    public static double ToPosition(double normalized, double areaSize, double blockSize)
        => (double.IsFinite(normalized) ? Math.Clamp(normalized, 0, 1) : 0)
            * AvailableRange(areaSize, blockSize);
}
