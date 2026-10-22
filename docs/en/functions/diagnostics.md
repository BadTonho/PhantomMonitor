> **Language:** English · [Português (Brasil)](../../funções/diagnostico.md)

# Application diagnostics

The diagnostics center combines recorded errors with the current health of sensor sources. It helps distinguish an application failure from a reading that the hardware, kernel, or driver did not publish.

## Access

- **Avalonia/Linux:** open **Diagnostics** in the sidebar. New error entries appear live; the **Sensors and sources** list is filled when the tab is opened and via **Refresh** (which also rereads the local file). Use **Open error log** to open it with the system's default application.
- **WPF/Windows:** click **Diagnostics: sensors and application errors** in the main window. The **Sensors and sources** tab follows current readings; **Application errors** shows recent entries and offers refresh and open-file actions.

Both views show up to 500 recent entries in chronological order, with timestamp, context, and message. Exception entries include technical details. The log stays on the local computer; the app does not upload reports.

## Location and retention

- **Linux/Avalonia:** `$XDG_DATA_HOME/PhantomMonitor/logs/errors.log`, normally `~/.local/share/PhantomMonitor/logs/errors.log`.
- **Windows/WPF:** `logs/errors.log` beside the executable.

The file is capped at about 1 MB, and consecutive duplicate messages are suppressed. If writing the file fails, the entry may still appear in the in-memory list for the current run.

## Sensors and sources

Known readings are reported as available or unavailable, with the source queried and an explanation when one can be determined. An optional sensor being absent is informational, not an application exception.

On Linux, details cover `procfs`, `sysfs`, `hwmon`, DRM, NVML, and the Phantom Vulkan layer. For NVIDIA devices, the view identifies the bound driver, reports whether `libnvidia-ml.so.1` loaded, and shows which metrics came from NVML or `hwmon`. For example, Nouveau without NVML may not provide utilization or VRAM metrics; temperatures also depend on channels exposed by the driver.

On Windows, the sensor tab lists hardware and readings discovered by LibreHardwareMonitor, PresentMon/ETW state, and availability of known sensor types.

## Limits

The app records exceptions it can observe and describes sources known to its readers. It cannot explain failures that never reach the process or infer values that a device or driver does not expose. Before sharing a diagnostic file, review it: local paths and messages can contain machine-specific information.
