> **Language:** English · [Português (Brasil)](../../funções/hardware-monitor.md)

# Hardware monitoring service

`HardwareMonitorService` reads Windows sensors through LibreHardwareMonitor, with WMI fallbacks where needed. The source lives in `PhantomMonitor.Core/Services/Windows/` (Phase 1 unification) and is selected by the cross-platform `SensorServiceFactory` in Core, shared by both frontends (WPF and Avalonia); the Linux counterpart lives in `Core/Services/Linux/`. The class is annotated `[SupportedOSPlatform("windows")]` — Windows-only APIs (WMI/ETW/PerformanceCounter) only get instantiated on the Windows side (OS guard inside the factory).

## Windows readings

The service collects CPU and GPU temperature, utilization, clocks, voltage, and power; GPU VRAM; RAM usage; disk activity and I/O; network throughput; fans; and motherboard sensors. CPU temperature falls back to ACPI thermal-zone sources only when LibreHardwareMonitor has no reading. RAM also has a WMI fallback. GPU-using processes are grouped by PID and cached briefly.

GPU core temperature is preferred over other chip diodes. Memory junction is published separately as `GpuVramTemperature`; AMD VRM, liquid, and PLX diodes are excluded from GPU core temperature. Integrated GPUs are enumerated before dedicated GPUs, and the dedicated card is instance 1. Disk activity uses `Total Activity`, not used-space percentage. Physical memory excludes virtual memory.

For CPU temperature, package/Tctl/Tdie/average sensors take priority, then maximum, then the remaining readings. GPU core sensors are preferred for `GpuTemperature`; memory/VRAM junction sensors map to `GpuVramTemperature`. The Windows reader uses `GPU Memory Used/Total` where available and falls back to D3D dedicated-memory values for integrated graphics. LibreHardwareMonitor `Data` readings for RAM are already in GB; `RamTotal` is calculated as used plus available. Per-core usage, disk activity, clocks, fan readings, and motherboard temperatures use the maximum where the canonical aggregate needs one value; disk and network throughput is summed.

The CPU-temperature fallback checks ACPI thermal-zone data, then the system thermal-zone performance counter, only when LibreHardwareMonitor has no temperature. It is labeled as an estimate and queried at most every ten seconds. RAM can fall back to WMI (`Win32_OperatingSystem`), with a reused performance counter. GPU process usage is read through WMI `GPUEngine`, grouped by process ID, names resolved, and cached for three seconds.

Windows also publishes battery readings when a battery exists and a session-scoped `WheaErrorCount` from the System event log. Desktop systems without a battery publish no `Battery*` keys. WHEA value zero is a valid reading. Monitor identities are decoded from EDID through WMI and cached for about 60 seconds.

## Per-instance series

Alongside canonical series (index 0), snapshots can include keys in the form `Category#index`, such as `GpuCore#1`, `CpuTemperature#3`, or `DiskRead#2`.

| Series family | Instances |
|---|---|
| `CpuPerCore#i`, `CpuTemperature#i`, `CpuClock#i` | CPU cores |
| `CpuPower#i` | CPU power domains |
| GPU categories, such as `GpuCore#i` | Dedicated GPU is 1, then integrated/other GPUs |
| `FanSpeed#i` | Motherboard, SuperIO, or EC fan slots |
| Disk categories (`DiskUsage`, `DiskRead`, `DiskWrite`, temperature, health, totals) | One per disk device |
| `NetworkUp#i`, `NetworkDown#i` | One per network interface |
| `MotherboardTemp#i`, `VrmTemperature#i`, `RailVoltage#i`, `RamTemperature#i` | Named hardware sensor slots |

Dedicated GPU is always instance 1. Disk and network indices follow the enumeration order. CPU core indices follow the source sensor's own core number; fans use their hardware slot/name. Canonical network totals (`NetworkUpTotal` and `NetworkDownTotal`) integrate each interface's observed throughput over the session.

CSV/JSON logging, the OSD allowlist, and alert `sensorIndex` consume these keys. See [inventory](hardware-inventory.md), [alerts](alerts.md), and [logging](logging.md).

The snapshot's series list is computed once per cycle, after the last write to values/series (custom formulas included), and handed to consumers — overlay, inventory, journal, CSV, and statistics share the same ordered list (canonical series first in `SensorType` enum order, then instances sorted). `SensorData.GetSeriesKeys()` returns that list when present and rebuilds the legacy way for snapshots that predate it.

## Battery and WHEA

Windows battery series include `BatteryLevel` (%), `BatteryVoltage` (V), `BatteryPower` (W; positive while discharging, negative while charging), `BatteryWearLevel` (%), and `BatteryAcPower` (1/0). WMI queries are cached for about ten seconds; unavailable properties use fallbacks where possible.

Battery voltage uses the reported battery status value, with design voltage as a fallback. Power is derived from discharge/charge rates. Wear is calculated from full-charge versus design capacity. Missing WMI properties are tolerated because battery drivers expose different subsets. `BatteryAcPower` reflects `PowerOnline`; a desktop with no battery publishes no `Battery*` series.

`WheaErrorCount` counts `Microsoft-Windows-WHEA-Logger` events since the monitoring session began. It is queried about every ten seconds, with a bounded reverse scan. Read permission failures are written to `logs/errors.log`. A rule with `Above` and threshold `0` detects that the count is nonzero; because the count is cumulative for the session, it can fire again after cooldown while remaining above zero, but it does not signal each new increment separately.

The counter is always present on supported Windows collection, including a legitimate value of zero. Event-log scanning stops at events older than the session start and is bounded to 5000 entries. If the provider is missing or access is denied, zero is retained and the failure is logged by `Whea.Reader`.

## Lifecycle and statistics

`Start`, `Stop`, `Update`, and `Dispose` are serialized. A concurrent tick is skipped, stop/dispose are idempotent, and `SensorUpdated` is raised outside the service lock. `SetPaused(true)` stops new cycles while preserving the last snapshot for the dashboard, overlay, inventory, and exporters; pause state is not persisted.

`SeriesStatsTracker` maintains per-series minimum, maximum, and arithmetic mean for the session. Non-finite samples are discarded. With per-series tuning, repeated published values are excluded so statistics follow each series' refresh cadence. Resetting statistics from the tray or inventory also writes a marker to active CSV/JSONL logs.

The tracker includes canonical and instance series, and FPS series such as `Fps`, `FrametimeMs`, and `FpsP1` after the FPS monitor contributes its current values. Its session statistics are independent from the approximately 60-second rolling window used for FPS average, minimum, and percentiles. A displayed average remains unavailable until the first sample. `SensorUpdated` fires for each published cycle and UI consumers dispatch it asynchronously so the timer thread is not blocked.

Custom formulas are evaluated at the end of each snapshot before statistics are committed. See [custom sensors](custom-sensors.md) and [per-series tuning](series-tuning.md).
