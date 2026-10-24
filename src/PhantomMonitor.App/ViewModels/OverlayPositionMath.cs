namespace PhantomMonitor.App.ViewModels;

/// <summary>Converts overlay positions between the stretched preview and a virtual 1920×1080 surface.</summary>
internal static class OverlayPositionMath
{
    public const double ReferenceWidth = 1920;
    public const double ReferenceHeight = 1080;

    public static double AvailableRange(double areaSize, double blockSize)
        => double.IsFinite(areaSize) && double.IsFinite(blockSize)
            ? Math.Max(0, areaSize - blockSize)
            : 0;

    public static double ToNormalized(double position, double areaSize, double blockSize, double originOffset = 0)
    {
        var range = AvailableRange(areaSize, blockSize);
        if (range <= 0 || !double.IsFinite(position)) return 0;

        var offset = double.IsFinite(originOffset) ? originOffset : 0;
        var normalized = (position - offset) / range;
        if (double.IsFinite(normalized)) return normalized;
        return normalized < 0 ? -double.MaxValue : double.MaxValue;
    }

    public static double ToPosition(double normalized, double areaSize, double blockSize, double originOffset = 0)
    {
        var range = AvailableRange(areaSize, blockSize);
        if (range <= 0) return 0;

        var offset = double.IsFinite(originOffset) ? originOffset : 0;
        var fraction = double.IsFinite(normalized) ? normalized : 0;
        var position = offset + (fraction * range);

        // Offsets and signed fractions may cross the area edge; clamp the final
        // visible coordinate so the block stays inside the followed area.
        if (double.IsNaN(position) || position <= 0) return 0;
        return position >= range ? range : position;
    }

    public static double ScaleOffset(double offset, double sourceAreaSize, double destinationAreaSize)
        => !double.IsFinite(offset) || !double.IsFinite(sourceAreaSize) || sourceAreaSize <= 0
            || !double.IsFinite(destinationAreaSize)
                ? 0
                : offset * destinationAreaSize / sourceAreaSize;

    public static double ToReference(double displayValue, double displayAreaSize, double referenceAreaSize)
        => !double.IsFinite(displayValue) || !double.IsFinite(displayAreaSize) || displayAreaSize <= 0
            || !double.IsFinite(referenceAreaSize)
                ? 0
                : displayValue * referenceAreaSize / displayAreaSize;

    public static double FromReference(double referenceValue, double referenceAreaSize, double displayAreaSize)
        => !double.IsFinite(referenceValue) || !double.IsFinite(referenceAreaSize) || referenceAreaSize <= 0
            || !double.IsFinite(displayAreaSize)
                ? 0
                : referenceValue * displayAreaSize / referenceAreaSize;

    public static double ToNormalizedFromDisplay(double displayPosition, double displayAreaSize,
        double displayBlockSize, double referenceAreaSize, double originOffset = 0)
    {
        if (!double.IsFinite(displayAreaSize) || displayAreaSize <= 0) return 0;
        var displayRange = AvailableRange(displayAreaSize, displayBlockSize);
        displayPosition = double.IsFinite(displayPosition)
            ? Math.Clamp(displayPosition, 0, displayRange)
            : 0;
        var referencePosition = ToReference(displayPosition, displayAreaSize, referenceAreaSize);
        var referenceBlockSize = ToReference(displayBlockSize, displayAreaSize, referenceAreaSize);
        return ToNormalized(referencePosition, referenceAreaSize, referenceBlockSize, originOffset);
    }

    public static double ToDisplayPosition(double normalized, double displayAreaSize, double displayBlockSize,
        double referenceAreaSize, double originOffset = 0)
    {
        if (!double.IsFinite(displayAreaSize) || displayAreaSize <= 0) return 0;
        var referenceBlockSize = ToReference(displayBlockSize, displayAreaSize, referenceAreaSize);
        var referencePosition = ToPosition(normalized, referenceAreaSize, referenceBlockSize, originOffset);
        return FromReference(referencePosition, referenceAreaSize, displayAreaSize);
    }
}
