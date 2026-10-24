> **Language:** English · [Português (Brasil)](../../funções/inventário-hardware.md)

# Hardware inventory

## Avalonia inventory

**Inventory** is a page in the Avalonia main window, accessible from the sidebar. It follows the active service snapshots and groups series into CPU, GPU, memory, disk, network, fans, battery, and FPS. CPU includes package and per-core power readings. GPU includes all graphics-card metrics, including clocks, memory, voltage, power, load, and GPU fan; Fans contains system fans. Each category is an expandable group, open by default, with a compact table: Sensor, Current, Minimum, Maximum, and Average. The friendly source label is shown as the sensor name; the technical key (for example, `CpuPerCore#1`) is available in a tooltip. Units appear with the current value and session statistics. The table and its groups fill the available width, keeping columns aligned through the right edge. Families with instances show a summary row. With one instance, the child row is hidden and its friendly name appears in the summary tooltip after a two-second hover. With two or more instances, the indented child rows stay visible, and an arrow on the summary collapses or expands only those rows without hiding the summary or its metrics. This applies to every category — category-group expansion remains separate. CPU Total keeps its total row and lists the cores below; if no aggregate is published, the summary uses the arithmetic mean of compatible instances. Custom formulas and sensors without instances remain standalone rows. Calculated means track session minimum, maximum, and average in memory, cleared by **Reset statistics**. Before a sample is available, statistics show `--` (NaN).

Since F1.4, the page also carries the WPF `InventoryWindow` additions:

- **"Reset statistics" button** in the page header: clears every per-series min/max/average (`ISensorService.ResetSeriesStats`) and writes a `#marker` row to the active CSV plus a matching JSONL note (`reset (inventory)`), with a transient confirmation next to the button and immediately cleared cells.
- **"Detected hardware" card:** the enumerated hardware tree (motherboard → SuperIO/EC, CPU, GPUs, subhardware), decoded from the `[HWR]` diagnostic lines through the Core `InventoryBuilder.BuildHardware`; nodes are expanded by default and the tree is rebuilt **only when the enumeration changes** (`Depth:Type:Name` signature). On Windows without administrator privileges the empty state points to starting as admin.
- **"Monitors (EDID)" card:** WMI instance path, manufacturer, model, product code, serial number, physical diagonal, manufacturing year/week, and a PRIMARY flag (same `WmiMonitorID`/`WmiMonitorBasicDisplayParams` source); the list only changes when the set of monitors changes. When no monitor is reported (desktops/VMs/RDP), the card is hidden and the JSON snapshot publishes `monitors: []`. If a WMI query fails, the app continues with an empty list and records one diagnostic entry per app run; a successful query with no monitors is not an error.
- The page summary shows `N hardware nodes · M monitor(s)` when present.

When the service publishes no value for an expected sensor type, the page marks it **Unavailable** (session cells `--`). Windows-only categories are not treated as missing on Linux. A published zero remains a valid reading; for example, a disconnected AC source is shown as disconnected rather than unavailable. The page updates with each snapshot and retains the last values (and session statistics, until reset) while collection is paused or stopped. It is read-only.

Rendering is stable across snapshots: the fixed groups (CPU, GPU, memory, disk, network, fans, battery, and FPS) and their rows persist as the same instances, and each reading is reconciled by key — only strings that actually changed re-render. Group expansion state is preserved while the app is running. The **Other** group, for series outside the fixed categories, is added to and removed from the end of the list depending on what the hardware publishes. Source: `ViewModels/SensorInventoryBuilder.cs` (declarative plan with Min/Max/Avg) and `ViewModels/MainWindowViewModel.Inventory.cs` (hardware tree, monitors, reset, and group/row reconciliation).

## Internal inventory details (inherited from the WPF window)

The WPF frontend — retired in Phase 2 — had a separate inventory window with
the tabs below; the unified app's **Inventory** page shares the same Core
foundation (`InventoryBuilder`, `SeriesStats`, `MonitorInfo`) and covers the
same information in expandable sensor groups and hardware/monitor sections
(summary above). The tabs remain as a reference for
the data and the reset markers. It helped diagnose missing sensors and discover
series keys for alerts and OSD configuration. Without an active snapshot it
reported that monitoring must be started.

### Published series

The table includes series key, source hardware label, formatted value, session minimum/maximum/average, unit, and whether a row is canonical. A local two-second dispatcher timer refreshes the table without changing the sensor service cadence. **Reset statistics** clears per-series values and writes a `#marker` row to active CSV and a corresponding note to JSONL.

Canonical rows have keys such as `GpuCore`; instance rows use complete keys such as `GpuCore#1`. The canonical flag identifies index 0 and the legacy series used by dashboard cards. Units follow `SensorUnits` (for example, °C, %, MHz, RPM, V, W, MB, GB/s, FPS, and ms). Until a series has a sample, its minimum, maximum, and average are displayed as `--`.

### Detected hardware

A tree shows enumerated hardware (motherboard, SuperIO/EC, CPU, GPUs, and subhardware) based on diagnostic data already produced by `HardwareMonitorService`; it does not enumerate the hardware a second time.

### Monitors (EDID/WMI)

The Windows tab lists active monitors from `WmiMonitorID` and `WmiMonitorBasicDisplayParams`, cached for about 60 seconds. Columns include WMI instance path, decoded manufacturer, model, product code, serial number, physical diagonal, manufacturing year/week, and primary-monitor flag. Optional properties are read defensively and inactive/ghost entries are excluded. The data is also included as `monitors` in the JSON snapshot, but not as numeric CSV or SDK series. Systems that successfully report no monitor return an empty list without an error; a failing WMI query returns an empty list and records one diagnostic entry per app run.

Manufacturer strings decode EDID PnP codes such as `DEL`, `SAM`, and `LGD`. The model comes from `UserFriendlyName`, with `ProductCodeID` as a fallback. The diagonal is calculated from the physical display width and height in centimeters and converted to inches; unavailable fields display `--`. The list is ordered by WMI instance path. `InventoryBuilder.BuildSeries` and `BuildHardware`, `HardwareMonitorService.GetMonitors`, and `MonitorInfo` provide this data.
