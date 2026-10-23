> **Language:** English · [Português (Brasil)](../../funções/diagnostico.md)

# Application diagnostics

The diagnostics center combines recorded errors with the current health of sensor sources. It helps distinguish an application failure from a reading that the hardware, kernel, or driver did not publish.

## Access

- **Unified app (Windows and Linux):** open **Diagnostics** in the sidebar. The page opens on **Summary**, showing monitoring status, the recent error count, and the newest occurrence. Use **Errors** and **Sensors** to open the detailed lists. New errors appear live; **Refresh** rereads the local file and refreshes status from the latest sample. **Open error log** opens the log with the system's default application.

The **Errors** option separates **Current GPU sensor warnings** from **Recorded errors**. Warnings show one row per unavailable metric with its GPU, source, and reason; they update with new samples, disappear when a reading returns, and are not written to `errors.log`. A general warning appears when no GPU is enumerated. Recorded errors show up to 500 recent entries in chronological order, with timestamp, context, and message. Exception entries include technical details. The log stays on the local computer; the app does not upload reports.

## Location and retention

- **Linux (unified app):** `$XDG_DATA_HOME/PhantomMonitor/logs/errors.log`, normally `~/.local/share/PhantomMonitor/logs/errors.log`.
- **Windows (unified app):** `%LOCALAPPDATA%\PhantomMonitor\logs\errors.log` (the old WPF frontend wrote `logs/errors.log` beside the executable).

The file is capped at about 1 MB, and consecutive duplicate messages are suppressed. If writing the file fails, the entry may still appear in the in-memory list for the current run.

## Sensors and sources

Known readings are reported as available or unavailable, with the source queried and an explanation when one can be determined. An optional sensor being absent is informational, not an application exception.

On Linux, details cover `procfs`, `sysfs`, `hwmon`, DRM, NVML, and the Phantom Vulkan layer. For each detected GPU, the view shows its vendor, driver, PCI ID, the source used for each metric, and why a reading is missing. For NVIDIA devices, it reports whether `libnvidia-ml.so.1` loaded and separates NVML, DRM, and `hwmon` metrics. For Intel `i915`/`xe`, it reports the state of `drm-engine-*` counters in `fdinfo`; the first sample establishes a baseline and utilization is calculated on the next interval. Restricted process visibility can prevent that reading without stopping other sensors. Nouveau can be identified even when the driver publishes no utilization, VRAM, or temperature channels; in that case values remain unavailable and diagnostics describe the sources checked.

On Windows, the sensor tab lists hardware and readings discovered by LibreHardwareMonitor, PresentMon/ETW state, and availability of known sensor types. The **Errors** tab also shows current warnings for the dashboard's six GPU metrics when LibreHardwareMonitor does not publish a valid reading.

## Limits

The app records exceptions it can observe and describes sources known to its readers. It cannot explain failures that never reach the process or infer values that a device or driver does not expose. Before sharing a diagnostic file, review it: local paths and messages can contain machine-specific information.
