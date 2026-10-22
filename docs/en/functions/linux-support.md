> **Language:** English · [Português (Brasil)](../../funções/suporte-linux.md)

# Native Linux support

Phantom Monitor's Linux frontend is built with Avalonia and reads platform sensors directly. It does not require MangoHud, GOverlay, or additional proprietary drivers. The WPF frontend remains the Windows application; platform-specific features differ.

> **Current scope:** `src/PhantomMonitor.App` is the Avalonia Linux frontend and consumes `LinuxSensorService` from `PhantomMonitor.Core`. It provides live monitoring, FPS, sensor inventory, custom-sensor and per-series editors, CSV/JSON/JSONL logging, settings and floating-overlay controls, an optional HTTP exporter (`/values`, `/health`), and alert rules with internal toasts and native D-Bus notifications. Linux alert scripts can run; sound and shutdown settings are retained but not executed. The Vulkan layer is built separately with CMake and `install_layer.sh`, outside the MSBuild solution. WPF and its Windows alert actions remain available on Windows during the transition. Since F1.1, the Avalonia app on Windows reads the same sensors through the cross-platform `SensorServiceFactory` (LibreHardwareMonitor + PresentMon via `Core/Services/Windows/`), and since F1.2 its dashboard has the temperature range pills, CPU/GPU detail lines, the Motherboard & fans card, the GPU process list (reported by Windows via WMI GPUEngine), the update badge in the STATUS box and the elevation banner with "Run as admin" (UAC), and since F1.3 the overlay on Windows gained the full OSD (per-app profiles, per-series graphs, text templates and true click-through with position by focused app and reinforced Topmost), while on Linux the overlay stays interactive and draggable with fixed Topmost — no follow, click-through, or per-app profile matcher.

## Architecture

```text
PhantomMonitor.App (Avalonia/Linux)
  └── PhantomMonitor.Core
      ├── ISensorService / IFpsMonitor
      └── LinuxSensorService
          ├── /proc/stat (CPU)
          ├── /proc/meminfo (RAM)
          ├── /sys/class/hwmon (temperatures and fans)
          ├── /sys/class/drm (GPU and VRAM)
          ├── /proc/diskstats and /proc/net/dev (disk and network)
          └── LinuxFpsMonitor → PhantomVulkanLayer (shared memory)

PhantomMonitor (WPF/Windows)
  └── HardwareMonitorService + FpsMonitor (PresentMon/ETW)
```

## Linux sensor sources

The service reads standard kernel interfaces without injecting code into other processes:

- **CPU usage:** deltas from `/proc/stat`, including one `CpuPerCore#i` series per core.
- **CPU clocks:** `scaling_cur_freq` under `/sys/devices/system/cpu/cpu*/cpufreq`; published per core as `CpuClock#i` when available.
- **RAM:** total and available physical memory from `/proc/meminfo`.
- **Temperatures and fans:** `/sys/class/hwmon/hwmon*`. CPU temperature comes from drivers such as `coretemp`, `k10temp`, and `zenpower`. GPU temperature labels distinguish edge, junction/hotspot, and memory/VRAM temperatures when the driver supplies labels. Motherboard/EC temperatures and fan slots are published when available.
- **Disk:** `/proc/diskstats` provides aggregate throughput and per-device `DiskRead#i`/`DiskWrite#i` series.
- **Network:** `/proc/net/dev` publishes aggregate and per-interface throughput; loopback is excluded. Session totals integrate observed throughput.
- **GPU utilization, VRAM, and temperature:** AMD metrics come from DRM files under `/sys/class/drm/card*/device`. NVIDIA metrics are read optionally through NVML (`libnvidia-ml.so.1`), supplied by the installed NVIDIA driver; this provides utilization, used/total VRAM, and core temperature without spawning `nvidia-smi`. Multiple cards receive per-instance series, and the canonical series uses the first available reading. Hot Spot and VRAM temperature appear only when the driver also exposes recognized `hwmon` labels. NVML is not bundled with the app.
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

Linux telemetry CSV/JSON/JSONL files and `errors.log` are stored under `~/.local/share/PhantomMonitor/logs/`, respecting `XDG_DATA_HOME`. The **Diagnostics** page shows recent records and sensor-source status; for NVIDIA it includes the bound driver, NVML, hwmon, and metrics reported by each source. The HTTP exporter is disabled by default and binds to loopback; `listenAll` exposes it on all interfaces without TLS or authentication. See [configuration](configuration.md), [logging](logging.md), [diagnostics](diagnostics.md), [alerts](alerts.md), and the [Telemetry SDK](../telemetry-sdk.md) for details.
