namespace PhantomMonitor.Models;

/// <summary>A single locally recorded application diagnostic entry.</summary>
public sealed record ErrorLogEntry(DateTime Timestamp, string Context, string Message)
{
    public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
}
