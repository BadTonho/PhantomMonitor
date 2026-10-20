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

    /// <summary>
    /// Consolida um snapshot nas estatísticas por série. Séries apontadas em
    /// <c>repeated</c> (repetições de cadência do SensorTuningEngine, P9.1) são
    /// puladas: o valor/publicação constante entre amostras não pode "amansar" o
    /// min/máx/média — a média da sessão amostra na cadência configurada, não no
    /// tick global.
    /// </summary>
    public void Commit(SensorData data, ISet<SensorKey>? repeated = null)
    {
        lock (_sync)
        {
            foreach (var key in data.GetSeriesKeys())
            {
                if (repeated is not null && repeated.Contains(key)) continue;

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
