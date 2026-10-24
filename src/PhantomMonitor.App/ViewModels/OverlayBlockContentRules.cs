using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

internal static class OverlayBlockContentRules
{
    public static IReadOnlyList<string> SelectSeriesIdentities(IReadOnlyList<string> identities, string itemPattern,
        IReadOnlyList<string>? profileSeries)
        => identities.Where(identity => IsSeriesSelected(identity, itemPattern, profileSeries))
            .ToArray();

    public static bool IsSeriesSelected(string identity, string itemPattern, IReadOnlyList<string>? profileSeries)
        => SensorKey.TryParse(identity, out var key)
            && (key.IsPrimary
                ? !itemPattern.Contains('*') && key.MatchesPattern(itemPattern)
                : key.MatchesPattern(itemPattern)
                    && (profileSeries is null || profileSeries.Count == 0 || profileSeries.Any(key.MatchesPattern)));
}
