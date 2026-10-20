namespace PhantomMonitor.App.ViewModels;

public sealed class InventoryGroupViewModel(string title, IReadOnlyList<InventoryRowViewModel> rows)
{
    public string Title { get; } = title;
    public IReadOnlyList<InventoryRowViewModel> Rows { get; } = rows;
}

public sealed class InventoryRowViewModel(
    string key,
    string source,
    string value,
    string unit,
    bool isAvailable)
{
    public string Key { get; } = key;
    public string Source { get; } = source;
    public string Value { get; } = value;
    public string Unit { get; } = unit;
    public bool IsAvailable { get; } = isAvailable;
}
