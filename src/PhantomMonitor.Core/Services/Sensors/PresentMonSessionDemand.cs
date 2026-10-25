namespace PhantomMonitor.Services;

internal sealed class PresentMonSessionDemand
{
    private readonly object _sync = new();
    private bool _collectionActive;
    private bool _discoveryActive;

    internal bool CollectionActive { get { lock (_sync) return _collectionActive; } }
    internal bool DiscoveryActive { get { lock (_sync) return _discoveryActive; } }
    internal bool IsRequired { get { lock (_sync) return _collectionActive || _discoveryActive; } }

    internal void SetCollectionActive(bool active)
    {
        lock (_sync) _collectionActive = active;
    }

    internal void SetDiscoveryActive(bool active)
    {
        lock (_sync) _discoveryActive = active;
    }
}
