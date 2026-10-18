using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

public interface ISensorService : IDisposable
{
    event Action<SensorData>? SensorUpdated;
    bool IsRunning { get; }
    void Start();
    void Stop();
    SensorData GetCurrentSnapshot();
}
