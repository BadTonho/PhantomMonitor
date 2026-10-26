namespace PhantomMonitor.Models;

/// <summary>A stable device reference and its currently detected model name.</summary>
public sealed record HardwareIdentity(string Category, string Id, string Name);
