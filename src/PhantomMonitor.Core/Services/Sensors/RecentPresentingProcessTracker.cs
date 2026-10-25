namespace PhantomMonitor.Services;

internal sealed class RecentPresentingProcessTracker
{
    private readonly object _sync = new();
    private readonly TimeSpan _lifetime;
    private readonly Dictionary<int, PresentingProcess> _processes = [];

    internal RecentPresentingProcessTracker(TimeSpan? lifetime = null)
        => _lifetime = lifetime ?? TimeSpan.FromSeconds(5);

    internal void Observe(int processId, string application, DateTimeOffset now)
    {
        if (processId <= 0 || string.IsNullOrWhiteSpace(application)) return;
        lock (_sync)
        {
            Prune(now);
            _processes[processId] = new PresentingProcess(processId, application, now);
        }
    }

    internal IReadOnlyList<PresentingProcess> Snapshot(DateTimeOffset now)
    {
        lock (_sync)
        {
            Prune(now);
            return _processes.Values.OrderBy(process => process.ProcessId).ToArray();
        }
    }

    internal void Clear()
    {
        lock (_sync) _processes.Clear();
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var processId in _processes
                     .Where(pair => now - pair.Value.LastPresentationUtc > _lifetime)
                     .Select(pair => pair.Key).ToArray())
            _processes.Remove(processId);
    }
}
