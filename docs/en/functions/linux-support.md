> **Language:** English · [Português (Brasil)](../../funções/suporte-linux.md)

# Native Linux support

Phantom Monitor's Linux support is built on the unified app with Avalonia and reads platform sensors directly. It does not require MangoHud, GOverlay, or additional proprietary drivers. The WPF frontend was retired in Phase 2 of the visual unification; the unified app is the interface on both systems.

> **Current scope:** `src/PhantomMonitor.App` is the unified Avalonia app and, on Linux, consumes `LinuxSensorService` from `PhantomMonitor.Core`. It provides live monitoring, FPS, full inventory (published series with session statistics, a hardware tree when the collector publishes `[HWR]` lines — Windows today — and a monitor card fed by Windows WMI; on Linux the list stays empty and the card is hidden), custom-sensor and per-series editors, CSV/JSON/JSONL logging, settings and floating-overlay controls, an optional HTTP exporter (`/values`, `/health`), an XDG autostart toggle (`~/.config/autostart/phantom-monitor.desktop`), and alert rules with internal toasts and native D-Bus notifications. Linux alert scripts can run; on Linux the desktop notification carries the alert sound and shutdown is not executed, while on Windows the unified app runs beep, script, and guarded shutdown at WPF parity (F1.5). Since F1.5 the unified app also has its own tray with the full menu (show, overlay, start with the system, reset statistics, pause, OSD profile pin, and exit) via StatusNotifier/DBus — on desktops without support the icon simply does not appear and the app stays functional —, single-instance enforcement through the `logs/session.lock` file lock, and the per-OS update applier (tarball/AppImage on Linux; portable zip + one-shot cmd on Windows). Global hotkeys are not registered on the Linux side yet (X11/XWayland is the future path; pure Wayland does not allow them by design). The Vulkan layer is built separately with CMake and `install_layer.sh`, outside the MSBuild solution. The WPF and its own Windows alert actions were retired in Phase 2 — the unified app covers both systems. Since F1.1, the Avalonia app on Windows reads the same sensors through the cross-platform `SensorServiceFactory` (LibreHardwareMonitor + PresentMon via `Core/Services/Sensors/Windows/`), and since F1.2 its dashboard has the temperature range pills, CPU/GPU detail lines, the Motherboard & fans card, the GPU process list (reported by Windows via WMI GPUEngine), the update badge in the STATUS box and the elevation banner with "Run as admin" (UAC), and since F1.3 the overlay on Windows gained the full OSD (per-app profiles, per-series graphs, text templates and true click-through with position by focused app and reinforced Topmost), while on Linux the overlay stays interactive and draggable with fixed Topmost — no follow, click-through, or per-app profile matcher.

## Architecture

```text
PhantomMonitor.App (Linux)
  └── PhantomMonitor.Core
      ├── ISensorService / IFpsMonitor
      └── LinuxSensorService
          ├── /proc/stat (CPU)
          ├── /proc/meminfo (RAM)
          ├── /sys/class/hwmon (temperatures and fans)
          ├── LinuxGpuSensorReader → DRM/PCI + hwmon + optional NVML
          │   └── /proc/<pid>/fdinfo (i915/xe usage when published)
          ├── /proc/diskstats and /proc/net/dev (disk and network)
          └── LinuxFpsMonitor → PhantomVulkanLayer (shared memory)

PhantomMonitor.App (Windows)
  └── PhantomMonitor.Core
      ├── Services/Sensors/Windows: HardwareMonitorService + FpsMonitor (PresentMon/ETW)
      └── Services/Platform/Windows: startup and Win32 integration
```

## Linux sensor sources

The implementation lives under `Services/Sensors/Linux/`. `LinuxSensorService` coordinates collection cycles and delegates CPU/memory, hwmon/power, and disk/network sampling to internal readers that own their counters. GPU enumeration and association with DRM, hwmon, and thermal zones are separated into `LinuxGpuSensorReader.Discovery.cs`.

The service reads standard kernel interfaces without injecting code into other processes:

- **CPU usage:** deltas from `/proc/stat`, including one `CpuPerCore#i` series per core.
- **CPU clocks:** `scaling_cur_freq` under `/sys/devices/system/cpu/cpu*/cpufreq`; published per core as `CpuClock#i` when available.
- **RAM:** total and available physical memory from `/proc/meminfo`.
- **Temperatures and fans:** `/sys/class/hwmon/hwmon*`. CPU temperature comes from drivers such as `coretemp`, `k10temp`, and `zenpower`. GPU hwmon entries are associated with their PCI/DRM device so Intel GPU temperatures are not classified as motherboard sensors. Driver channel labels identify `GpuTemperature#i`, `GpuHotSpot#i`, and `GpuVramTemperature#i`; `GpuFan` and `FanSpeed#i` are available when a `fan*_input` channel exists. Motherboard/EC temperatures are also published when available.
- **Disk:** `/proc/diskstats` provides aggregate throughput and per-device `DiskRead#i`/`DiskWrite#i` series.
- **Network:** `/proc/net/dev` publishes aggregate and per-interface throughput; loopback is excluded. Session totals integrate observed throughput.
- **GPU utilization, VRAM, and temperature:** AMD uses aggregate DRM attributes (`gpu_busy_percent`, `mem_info_vram_used/total`) and associated `hwmon` channels. NVIDIA uses NVML (`libnvidia-ml.so.1`) when the installed driver provides it; Nouveau and other drivers can still supply DRM or `hwmon` readings. Intel associates `hwmon` by PCI path and reads `drm-engine-*` from `/proc/<pid>/fdinfo` for `i915`/`xe` usage when available. Usage is the busiest engine normalized by the sample interval and driver-reported capacity; the first sample establishes a baseline, so usage appears on the next interval. Processes owned by other users or hidden by namespaces may not be visible. Memory used/total comes only from per-device aggregate sources: per-process statistics are not added together because buffers can be shared, and system RAM is never presented as VRAM. Multi-GPU devices receive per-card series while existing canonical sensor keys remain. Hot Spot and VRAM temperature require explicitly labeled driver `hwmon` channels. Each metric can be unavailable when a device/driver does not expose it; Diagnostics reports the driver, source, and reason per device and metric. No administrator privileges or external utilities are required, and NVML is not bundled with the app.
- **Battery and AC power:** `/sys/class/power_supply/BAT*` and mains/UPS entries provide charge, voltage, power, wear, and AC-connected state. Power is positive while discharging and negative while charging. Systems without a power-supply device publish no battery series.

Unavailable values display as `—` in the dashboard; a reported zero remains a valid value.

## FPS and frametime through Vulkan

`PhantomVulkanLayer` is a native C++ implicit Vulkan layer in `src/PhantomVulkanLayer/`. The Vulkan loader discovers it automatically. The layer intercepts `vkQueuePresentKHR`, measures frame intervals with a monotonic clock, and writes data to a per-process POSIX shared-memory object. The C# `LinuxFpsMonitor` reads that data and calculates rolling FPS metrics and frametime history.

Shared memory is normally named `/dev/shm/phantom_fps_<pid>`, with a `/tmp` fallback. Objects use user-only permissions, are removed during Vulkan teardown, and are ignored when their process ID is no longer present. The C++ and C# packed data structures must remain layout-compatible. Physical-device function forwarding (`Phantom_vkGetPhysicalDeviceProcAddr`, negotiate interface v2) is resolved from each instance's own chain GIPA, not the global fallback; with two games running simultaneously, no instance receives functions resolved by another instance's chain. The C# reader also defends against torn reads: the writer publishes `instant_fps × frametime_ms ≈ 1000` because both values come from the same frame delta; a pair that fails that relation means a read raced the present write. A reread on the same object resolves it, and a persistently inconsistent cycle is discarded instead of inserting a false minimum or percentile into the statistics.

## Run and install the layer

Run the dashboard from the repository root:

```bash
dotnet run --project src/PhantomMonitor.App
```

The dashboard starts sensor collection automatically. Build and install the Vulkan layer when FPS data is needed:

```bash
cd src/PhantomVulkanLayer
./install_layer.sh
```

The script builds `libVkLayer_phantom_monitor.so`, installs it under `~/.local/lib`, and registers the implicit-layer manifest at `~/.local/share/vulkan/implicit_layer.d/phantom_vulkan_layer.json`. Older manifests in the nonstandard `explicit_layers.d` location are removed by the installer.

The layer loads for Vulkan processes but publishes telemetry only when enabled for that process. `DISABLE_PHANTOM_OVERLAY=1` disables it for a session and takes precedence over the enable variable.

Steam launch options:

```bash
ENABLE_PHANTOM_OVERLAY=1 %command%
```

For Lutris, Heroic, or a terminal:

```bash
ENABLE_PHANTOM_OVERLAY=1 ./game-executable
```

## Linux frontend notes

The floating overlay follows `showOverlay` and existing `OverlayConfig` fields. On Wayland, the compositor controls its initial position and fullscreen stacking; transparency can also depend on compositor support. On Windows, the Avalonia overlay is now a true click-through OSD that follows the focused app (30 ms) with a Topmost reinforcement (500 ms); on Linux, the overlay remains interactive and draggable with fixed Topmost — click-through, follow, and the per-app profile matcher are Windows-only behaviors.

Linux telemetry CSV/JSON/JSONL files and `errors.log` are stored under `~/.local/share/PhantomMonitor/logs/`, respecting `XDG_DATA_HOME`. The **Diagnostics** page shows recent records, current warnings for unavailable GPU metrics in **Errors**, and detailed source status in **Sensors**; for NVIDIA it includes the bound driver, NVML, hwmon, and metrics reported by each source. The HTTP exporter is disabled by default and binds to loopback; `listenAll` exposes it on all interfaces without TLS or authentication. See [configuration](configuration.md), [logging](logging.md), [diagnostics](diagnostics.md), [alerts](alerts.md), and the [Telemetry SDK](../telemetry-sdk.md) for details.
