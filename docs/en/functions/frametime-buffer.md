> **Language:** English · [Português (Brasil)](../../funções/frametime-buffer.md)

# FrametimeBuffer

`FrametimeBuffer` is a thread-safe circular buffer for frametime history. `FpsMonitor` uses it for the latest frame samples, charts, and statistics. OSD per-series sparklines also use this class, with an independent capacity from `overlay.graphs[].window`.

## Behavior and methods

- Configurable capacity; the default is 600 samples.
- `Add(float value)` appends a value and overwrites the oldest sample when full.
- `Clear()` empties the buffer.
- `ToArray()` returns a consistent copy in chronological order.
- `GetStats()` returns the minimum, maximum, and average of stored values.

Internal locking protects writes from PresentMon and reads from sensor/UI threads, so callers never receive a partially updated snapshot.

The buffer is also used by the dashboard and overlay charts, including frametime and the WPF OSD sparklines for arbitrary published series. Each series graph owns its configured history capacity, so clearing or wrapping one history does not alter another.
