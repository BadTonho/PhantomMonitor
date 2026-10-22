> **Language:** English · [Português (Brasil)](../../funções/sensor-formatter.md)

# SensorFormatter

`SensorFormatter` is a shared static utility for metric formatting in the main dashboard and OSD, keeping values consistent without duplicating formatting logic.

## Formatting helpers

- `FormatBytes(bytes)`: human-readable byte units such as `1.5 KB`, `12.3 MB`, or `1.20 GB`.
- `FormatBytesPerSec(bytes)`: the same scale with `/s` for disk/network throughput.
- `FormatClock(mhz)`: MHz or GHz, such as `850 MHz` or `4.15 GHz`.
- `FormatFan(value)`: percentage for values up to 100, otherwise RPM; missing values use `--`.
- `BuildCpuDetail(data)` and `BuildGpuDetail(data)`: compact CPU/GPU detail rows, or null when no detail values are available.
- `BuildFanSummary(data)`: a summary such as `Fans: GPU 45% | System 1200 RPM`.
- `FormatSeries(key, value)`: formats any series using its category unit. The Avalonia inventory calls the overload that displays valid zero values; callers that use the legacy behavior can treat values at or below zero as unavailable.

Consumers include the WPF dashboard cards and overlay, WPF inventory, and the Avalonia inventory and floating overlay.
