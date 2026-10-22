> **Language:** English · [Português (Brasil)](../../funções/sensores-customizados.md)

# Custom sensors (user formulas)

Each configured formula becomes a telemetry series named `Custom#i`. Formulas are evaluated at the end of each snapshot, before session statistics are committed, so they can be consumed like native series by CSV/JSON, the telemetry SDK, HTTP export, inventory, OSD, and alerts.

## Configuration

On Linux, the Avalonia **Custom sensors** page can add, edit, remove, and reorder entries. It validates required unique names, formula syntax, and the 64-sensor limit before saving. The WPF app consumes the same schema.

```json
"customSensors": [
  { "name": "DeltaTemp", "formula": "{GpuTemperature} - {CpuTemperature}", "unit": "°C" },
  { "name": "VramShare", "formula": "{GpuMemoryUsed} / {GpuMemoryTotal} * 100", "unit": "%" },
  { "name": "PeakGpu", "formula": "max({GpuTemperature#1}, {GpuTemperature#2})" },
  { "name": "AboveAmbient", "formula": "{DeltaTemp} - 22" }
]
```

`name` is required and becomes the series label and a possible formula/OSD reference. `formula` is required. `unit` is optional and is displayed in the label only. `Custom#i` uses the formula's one-based position, so reordering changes indices used by alerts.

## Formula syntax

- Operators: `+`, `-`, `*`, `/`, parentheses, and unary `-` (unary `+` is also accepted and discarded).
- Decimal numbers use a dot, such as `0.5`; commas separate function arguments.
- Functions: variadic `min(a, b, ...)`, `max(a, b, ...)`, and `abs(a)`.
- References use braces. An explicit key such as `{GpuTemperature#1}` addresses a series. If a canonical key is missing, its first published instance may be used. Wildcards such as `{GpuTemperature#*}` and `{FanSpeed#*}` resolve to the first match. A friendly custom-sensor name such as `{DeltaTemp}` matches its label or base label and can reference earlier formulas in the list.

The friendly-name match is case-insensitive and accepts the base label before a unit suffix in parentheses, so the display label `DeltaTemp (°C)` can still be referenced as `{DeltaTemp}`. Wildcard resolution follows `SensorKey` match ordering, from canonical series to instances.

Formulas are evaluated in configuration order. A formula can reference custom sensors that appear before it, but not later entries.

## Missing values and consumers

If any reference is unresolved, or the result is non-finite (for example, division by zero), the formula publishes no value for that snapshot. The series returns when inputs become available. Inventories, graphs, alerts, logs, and statistics treat it as absent for that cycle.

Custom series appear as `Custom#1`, `Custom#2`, and so on. Use `Custom#*` in `overlay.shownSeries` or reference a friendly name in OSD templates. Alerts target `sensor: "Custom"` with the matching `sensorIndex`. CSV, JSON, the SDK, and HTTP export include the value and label; the formula itself stays in `config.json`.

## Runtime and limitations

Formulas are compiled once into an AST with a maximum of 256 nodes. At most 64 formulas are allowed. The shared engine runs in Windows and Linux, using whichever series each platform provides. The Avalonia **Save and apply** action persists settings and recreates the Linux sensor service: active collection restarts with reset session statistics, paused collection remains paused, and stopped collection remains stopped. The editor checks syntax, not whether physical readings are present at save time. Reordering may change alert indices, so the editor warns about order changes.

Each formula is compiled once by `CustomSensorEngine`; per-snapshot work is reference lookup and arithmetic. A syntactically valid formula can be saved when its physical source sensor is currently absent. The output simply remains unavailable until all inputs resolve. When the service is recreated, runtime calculation and session-statistic state starts fresh.

The language has no modulo, exponentiation, conditionals, or historical-series references. `min` and `max` can express simple bounds, for example `min({CpuPerCore} + 10, 100)`. Units affect labels only and are not part of calculations.
