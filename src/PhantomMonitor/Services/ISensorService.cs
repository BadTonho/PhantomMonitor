using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

public interface ISensorService : IDisposable
{
    event Action<SensorData>? SensorUpdated;
    bool IsRunning { get; }
    void Start();
    void Stop();
    SensorData GetCurrentSnapshot();

    /// <summary>Zera as estatísticas por série acumuladas na sessão (min/max/avg).</summary>
    void ResetSeriesStats();
}
