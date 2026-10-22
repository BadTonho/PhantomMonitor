> **Language:** English · [Português (Brasil)](../../funções/inventário-hardware.md)

# Hardware inventory

## Avalonia inventory (Linux)

**Inventory** is a page in the Avalonia main window, accessible from the sidebar. It follows Linux service snapshots and groups series into CPU, GPU, memory, disk, network, fans, battery, and FPS. Each row shows its key (for example, `CpuPerCore#1`), source label, current value, and unit. Per-core, per-device, per-interface, and other instance series are shown individually.

When the Linux service publishes no value for an expected Linux sensor type, the page marks it **Unavailable**. Windows-only categories are not treated as missing. A published zero remains a valid reading; for example, a disconnected AC source is shown as disconnected rather than unavailable. The page updates with each snapshot and retains the last values while collection is paused or stopped. It is read-only.

Rendering is stable across snapshots: the fixed groups (CPU, GPU, memory, disk, network, fans, battery, and FPS) and their rows persist as the same instances, and each reading is reconciled by key — only strings that actually changed re-render. The **Other** group, for series outside the fixed categories, is added to and removed from the end of the list depending on what the hardware publishes. Source: `ViewModels/SensorInventoryBuilder.cs` (declarative plan) and `MainWindowViewModel.ReconcileInventoryGroups`.

## WPF inventory (Windows)

The remaining features in this section describe the WPF inventory window and are not part of the Avalonia Linux page. Open it from the sidebar. It can help diagnose missing sensors and discover series keys for alerts and OSD configuration. Without an active snapshot it reports that monitoring must be started.

### Published series

The table includes series key, source hardware label, formatted value, session minimum/maximum/average, unit, and whether a row is canonical. A local two-second dispatcher timer refreshes the table without changing the sensor service cadence. **Reset statistics** clears per-series values and writes a `#marker` row to active CSV and a corresponding note to JSONL.

Canonical rows have keys such as `GpuCore`; instance rows use complete keys such as `GpuCore#1`. The canonical flag identifies index 0 and the legacy series used by dashboard cards. Units follow `SensorUnits` (for example, °C, %, MHz, RPM, V, W, MB, GB/s, FPS, and ms). Until a series has a sample, its minimum, maximum, and average are displayed as `--`.

### Detected hardware

A tree shows enumerated hardware (motherboard, SuperIO/EC, CPU, GPUs, and subhardware) based on diagnostic data already produced by `HardwareMonitorService`; it does not enumerate the hardware a second time.

### Monitors (EDID/WMI)

The Windows tab lists active monitors from `WmiMonitorID` and `WmiMonitorBasicDisplayParams`, cached for about 60 seconds. Columns include WMI instance path, decoded manufacturer, model, product code, serial number, physical diagonal, manufacturing year/week, and primary-monitor flag. Inactive/ghost entries are excluded. The data is also included as `monitors` in the JSON snapshot, but not as numeric CSV or SDK series. Systems that report no monitor return an empty list without an error.

Manufacturer strings decode EDID PnP codes such as `DEL`, `SAM`, and `LGD`. The model comes from `UserFriendlyName`, with `ProductCodeID` as a fallback. The diagonal is calculated from the physical display width and height in centimeters and converted to inches; unavailable fields display `--`. The list is ordered by WMI instance path. `InventoryBuilder.BuildSeries` and `BuildHardware`, `HardwareMonitorService.GetMonitors`, and `MonitorInfo` provide this data.
