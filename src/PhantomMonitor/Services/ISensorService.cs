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

    /// <summary>Monitoramento em pausa (P6.7): nenhum ciclo roda, o último snapshot permanece.</summary>
    bool Paused { get; }

    /// <summary>Pausa/retoma a coleta (runtime, idempotente; não persiste no config).</summary>
    void SetPaused(bool paused);
}
