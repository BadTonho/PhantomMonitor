namespace PhantomMonitor.App.ViewModels;

public sealed class OverlaySeriesPatternViewModel(string pattern)
{
    public string Pattern { get; } = pattern;
}
