> **Language:** English · [Português (Brasil)](../../funções/diagnostico.md)

# Application diagnostics

The diagnostics center combines recorded errors with the current health of sensor sources. It helps distinguish an application failure from a reading that the hardware, kernel, or driver did not publish.

## Access

- **Unified app (Windows and Linux):** open **Diagnostics** in the sidebar. The page opens on **Summary**, showing monitoring status, the recent error count, and the newest occurrence. Use **Errors** and **Sensors** to open the detailed lists. New errors appear live; **Refresh** rereads the local file and refreshes sensor sources. **Open error log** opens the log with the system's default application.

The **Errors** option shows up to 500 recent entries in chronological order, with timestamp, context, and message. Exception entries include technical details. The log stays on the local computer; the app does not upload reports.

## Location and retention

- **Linux (unified app):** `$XDG_DATA_HOME/PhantomMonitor/logs/errors.log`, normally `~/.local/share/PhantomMonitor/logs/errors.log`.
- **Windows (unified app):** `%LOCALAPPDATA%\PhantomMonitor\logs\errors.log` (the old WPF frontend wrote `logs/errors.log` beside the executable).

The file is capped at about 1 MB, and consecutive duplicate messages are suppressed. If writing the file fails, the entry may still appear in the in-memory list for the current run.

## Sensors and sources

Known readings are reported as available or unavailable, with the source queried and an explanation when one can be determined. An optional sensor being absent is informational, not an application exception.

On Linux, details cover `procfs`, `sysfs`, `hwmon`, DRM, NVML, and the Phantom Vulkan layer. For NVIDIA devices, the view identifies the bound driver, reports whether `libnvidia-ml.so.1` loaded, and shows which metrics came from NVML or `hwmon`. For example, Nouveau without NVML may not provide utilization or VRAM metrics; temperatures also depend on channels exposed by the driver.

On Windows, the sensor tab lists hardware and readings discovered by LibreHardwareMonitor, PresentMon/ETW state, and availability of known sensor types.

## Limits

The app records exceptions it can observe and describes sources known to its readers. It cannot explain failures that never reach the process or infer values that a device or driver does not expose. Before sharing a diagnostic file, review it: local paths and messages can contain machine-specific information.
