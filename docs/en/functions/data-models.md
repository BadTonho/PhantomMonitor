> **Language:** English · [Português (Brasil)](../../funções/modelos-dados.md)

# Data models

## `SensorType`

The sensor enum covers CPU (`CpuTotal`, per-core, temperature, power, clock, voltage); GPU (core, memory and VRAM capacity/use, temperature, hotspot, memory junction, clocks, voltage, fan, power); RAM; disk activity, throughput, temperature, health, and totals; network throughput and session totals; motherboard, fan, VRM, and rail sensors; battery level, voltage, power, wear, and AC state; FPS, frametime, rolling lows/percentiles, and PresentMon busy/wait/latency metrics; custom formulas; and the Windows-only `WheaErrorCount`.

The concrete enum names include `CpuTotal`, `CpuPerCore`, `CpuTemperature`, `CpuPower`, `CpuClock`, `CpuVoltage`; `GpuCore`, `GpuMemory`, `GpuMemoryUsed`, `GpuMemoryTotal`, `GpuTemperature`, `GpuHotSpot`, `GpuVramTemperature`, `GpuCoreClock`, `GpuMemoryClock`, `GpuVoltage`, `GpuFan`, `GpuPower`; `RamUsage`, `RamAvailable`, `RamUsed`, `RamTotal`, `RamTemperature`; `DiskUsage`, `DiskRead`, `DiskWrite`, `DiskTemperature`, `DiskHealth`, `DiskTotalRead`, `DiskTotalWritten`; `NetworkUp`, `NetworkDown`, `NetworkUpTotal`, `NetworkDownTotal`; `MotherboardTemp`, `FanSpeed`, `VrmTemperature`, `RailVoltage`; battery fields; `Fps`, `FrametimeMs`, `FpsAvg`, `FpsMin`, `Fps1Low`, `Fps01Low`, `FpsMax`, `FpsP1`, `FpsP99`, `FrametimeP1`, `FrametimeP99`, `GpuBusy`, `CpuBusy`, `CpuWait`, and `PresentLatency`; `Custom`; and `WheaErrorCount`.

Categories can also be published as `Category#index` instance series. Index 0 is the canonical/aggregate series. See [hardware monitoring](hardware-monitor.md) for examples.

## `SensorData`

`SensorData` represents one snapshot:

- `Timestamp`: reading time.
- `Values` and `Labels`: canonical values and labels.
- `Series` and `SeriesLabels`: per-instance telemetry keyed by `SensorKey`.
- `SeriesStats`: session minimum, maximum, and average per series.
- `Monitors`: connected display identities where provided.
- `GpuProcesses`, `SensorDebugInfo`, and `FrametimeHistory`: runtime/dashboard data not serialized in the JSON snapshot.
- Calculated convenience properties include CPU/GPU usage and temperature, RAM usage/availability, FPS, frametime, averages, lows, and maximum.

The GPU process list, sensor diagnostics, and frametime buffer are runtime-facing properties and are not serialized in `sensors_latest.json`. Monitor identity is serialized separately under `monitors`; sensor statistics are under `seriesStats`.

## `SensorKey`

`SensorKey` represents a category and instance index. `GpuCore` is the canonical series (index 0); `GpuCore#1` is an instance. `TryParse` and `Parse` validate textual keys. `MatchesPattern` supports `*`, prefix/suffix wildcards, and patterns such as `CpuTemperature#*`, case-insensitively. `SensorUnits.Of(type)` returns the standard display unit for a category.

## `MonitorInfo`

Windows monitor identity is decoded from active EDID/WMI data and included in the JSON snapshot and inventory tab. Properties include manufacturer, model, product code, serial, diagonal in inches, manufacturing year/week, primary status, and formatted diagonal/date text. A zero size/year means the field was not reported.

The model's `DiagonalText` and `MadeText` properties format inventory display values such as `27.5"` and `2023 w24`; missing data is displayed as `--`. Monitor records are sorted by WMI instance path and excluded when the monitor is not active.

## Alert models

`AlertComparison` is `Above` or `Below`. `AlertRule` stores enabled state, sensor type and index, comparison, threshold, per-rule cooldown, popup, sound, script and arguments, shutdown, and message. `AlertsConfig` stores the global enabled flag and cooldown, the shutdown safety switch, and rules. See [alerts](alerts.md).

`GpuProcessInfo` carries the process name, GPU usage percentage, and memory usage in MB.

Sensor units are obtained from `SensorUnits.Of(type)` and include °C, %, MHz, RPM, V, W, MB, GB, bytes per second, bytes, FPS, milliseconds, and `err` for WHEA counts.
