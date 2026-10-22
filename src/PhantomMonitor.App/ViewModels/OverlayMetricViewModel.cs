namespace PhantomMonitor.App.ViewModels;

public sealed class OverlayMetricViewModel(string name, string value, bool showFrametimeGraph = false)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
    public bool ShowFrametimeGraph { get; } = showFrametimeGraph;
}
