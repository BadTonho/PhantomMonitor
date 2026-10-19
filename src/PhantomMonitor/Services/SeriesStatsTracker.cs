using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Acumula min/max/avg por série a cada ciclo publicado (<see cref="Commit"/>).
/// Conta uma amostra por snapshot (não por escrita num mapper): sobrescritas de
/// <c>PutPreferred</c>/<c>PutMax</c> dentro do mesmo ciclo não inflariam a média —
/// o valor que entra é o que de fato chega ao overlay/CSV/SDK.
/// </summary>
public sealed class SeriesStatsTracker
{
    private readonly Dictionary<SensorKey, SeriesStats> _stats = [];
    private readonly object _sync = new();

    public void Commit(SensorData data)
    {
        lock (_sync)
        {
            foreach (var key in data.GetSeriesKeys())
            {
                var value = data.GetValue(key);
                if (!double.IsFinite(value)) continue;

                if (!_stats.TryGetValue(key, out var stats))
                    _stats[key] = stats = new SeriesStats();
                stats.Add(value);
            }
        }
    }

    /// <summary>
    /// Cópia anexada ao snapshot publicado: o tracker continua mutando entre ciclos,
    /// e consumidores da janela de inventário leriam células em mutação sem a cópia.
    /// </summary>
    public Dictionary<SensorKey, SeriesStats> CopySnapshot()
    {
        lock (_sync)
        {
            var copy = new Dictionary<SensorKey, SeriesStats>(_stats.Count);
            foreach (var (key, stats) in _stats)
            {
                copy[key] = new SeriesStats { Min = stats.Min, Max = stats.Max, Sum = stats.Sum, Count = stats.Count };
            }
            return copy;
        }
    }

    public void Reset()
    {
        lock (_sync) _stats.Clear();
    }
}
