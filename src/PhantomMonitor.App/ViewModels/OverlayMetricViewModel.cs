namespace PhantomMonitor.App.ViewModels;

public sealed class OverlayMetricViewModel(string name, string value)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
}
