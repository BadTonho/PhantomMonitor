> **Language:** English · [Português (Brasil)](../../funções/ajuste-de-series.md)

# Per-series sampling and smoothing (P9.1)

Configure the publication interval and value smoothing for each telemetry series. Physical sensor reads still occur on the global `updateIntervalMs` tick; this layer decides when each published value refreshes and whether a moving average should reduce jitter.

## Configuration

```json
"sensorTuning": [
  { "series": "CpuTemperature", "intervalMs": 2000, "smoothing": 3 },
  { "series": "WheaErrorCount", "intervalMs": 30000 },
  { "series": "CpuTemperature#*", "intervalMs": 2000 },
  { "series": "Custom#1", "smoothing": 5 }
]
```

| Field | Meaning |
|---|---|
| `series` | `SensorKey` pattern: exact canonical key (`GpuTemperature`), instance (`GpuTemperature#1`), instance family (`CpuTemperature#*`), sensor family (`Gpu*`, canonical and instances), or custom series (`Custom#1`). Matching is case-insensitive. |
| `intervalMs` | Series refresh interval, 100–3,600,000 ms. `0` publishes on every global tick (default). Normalization raises positive values below the global tick because a faster per-series interval cannot produce additional physical samples. |
| `smoothing` | Moving-average window over fresh samples, 2–60. `0` or `1` disables smoothing (default). |

There can be at most 64 rules; entries without `series` are discarded. If several rules match one series, the **last matching rule wins**, including its interval, smoothing, and fresh-sample state.

## Runtime behavior

1. Between refreshes, the series repeats its last published value. CSV, JSON, the SDK, and the journal therefore keep a continuous timeline, while the inventory and OSD remain stable.
2. Per-series statistics skip repeated values and only count fresh reads.
3. Smoothing is a moving average over fresh samples. Each series has its own circular buffer; non-finite values are ignored.
4. A series' first sample is always fresh. If a series disappears, no stale state is created; when it returns, it is sampled again.
5. Custom formulas are evaluated before tuning, so they use raw readings. The tuning engine then treats `Custom#i` like any other series.

The shared `SensorTuningEngine` is used by the Windows `HardwareMonitorService` and Linux `LinuxSensorService`, before statistics are committed. Alerts follow the publication cadence, so a rule with a 30-second interval may react up to 30 seconds after the raw threshold is crossed. OSD history graphs plot published values and therefore show plateaus between slow refreshes.

The first reading for a series is always fresh and arms its refresh clock. When a series disappears, it is ignored rather than retaining a repeatable phantom value; if it later returns, its first new reading is fresh. Custom sensors are evaluated before this stage so their formulas see raw source readings, after which `Custom#i` can itself be smoothed or published less often.

Avoid smoothing cumulative counters such as `DiskTotalRead`, `DiskTotalWritten`, `NetworkUpTotal`, and `NetworkDownTotal`; averaging totals has no useful physical meaning. FPS and PresentMon percentiles already use their own rolling windows and EMA, so additional smoothing is usually unnecessary.

The engine does not infer whether a series is an instantaneous measurement or a counter. Configuration authors are responsible for selecting appropriate targets. Network session totals are integrated from throughput on the global tick, so delaying or averaging those totals is not useful.

## Avalonia editor

On Linux, **Per-series tuning** edits `sensorTuning` without manually editing JSON. Choose a detected series, retain a saved target that is currently missing, or enter a key or pattern such as `Gpu*` or `CpuTemperature#*`. Unmatched patterns are kept for future sensors. The editor supports up to 64 entries, intervals of `0` or 100–3,600,000 ms, smoothing from 0–60, and rule reordering. As in the engine, the last matching rule wins.

**Save and apply** writes the configuration and recreates the Linux sensor service. A save failure leaves the active rules unchanged. A stopped collector stays stopped and a paused collector stays paused. Recreating an active service starts a new sensor session and resets its statistics. WPF continues to consume the same `sensorTuning` schema.
