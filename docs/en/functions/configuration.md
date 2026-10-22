> **Language:** English · [Português (Brasil)](../../funções/configuracao.md)

# App configuration

Phantom Monitor uses JSON configuration. In a development checkout, `config.json` is created in the repository root; installed builds use a per-user location.

## Main settings

| Setting | Purpose |
|---|---|
| `updateIntervalMs` | Global sampling interval in milliseconds (default 1000; minimum 100). |
| `logToCsv`, `logToJson`, `logToJsonJournal` | Enable CSV, latest-snapshot JSON, or append-only JSONL logging. See [logging](logging.md). |
| `showOverlay` | Show the overlay at startup; Avalonia also uses it to control the floating Linux overlay from the dashboard. |
| `toggleOverlayHotkey`, `pauseMonitorHotkey` | Global Windows hotkeys for overlay visibility and monitoring pause/resume. See [global hotkeys](global-hotkey.md). |
| `overlayOpacity`, `overlayPosition`, `overlay` | Global opacity, initial position, and overlay settings. |
| `sensors` | Sensor categories enabled by the model; not all current frontends consume these switches. |
| `alerts` | Alert rules and actions. Avalonia edits rules, cooldowns, internal notifications, and scripts; Linux preserves but does not execute sound/shutdown actions. See [alerts](alerts.md). |
| `telemetryExport` | Optional HTTP exporter: `{ "enabled": false, "port": 7317, "listenAll": false }`; provides `/values` and `/health`. See the [Telemetry SDK](../telemetry-sdk.md). |
| `profiles` | Windows OSD per-application overrides: name, process match, position, width, font, opacity, accent, series, and visibility. See [overlay](overlay.md). |
| `overlay.templates`, `overlay.graphs` | Custom OSD text lines and per-series history sparklines; Windows feature. |
| `customSensors` | User formulas in the form `{ name, formula, unit? }`, published as `Custom#i`. Avalonia has an editor. See [custom sensors](custom-sensors.md). |
| `sensorTuning` | Per-series publication interval and smoothing rules. See [per-series tuning](series-tuning.md). |

## `OverlayConfig`

`width` sets the panel width. `showCpu`, `showGpu`, `showRam`, `showDisk`, `showNetwork`, `showFans`, `showMotherboard`, `showFps`, and `showFrametimeGraph` control visible groups. `compactMode`, `accentColor`, and `fontSize` control appearance. `shownSeries` is a wildcard allowlist for per-instance series; the default is `["Gpu*", "FanSpeed*", "Disk*"]`. Supported patterns include `*`, prefixes, suffixes, and `#*`, matched case-insensitively. An empty or missing list is normalized to the defaults.

The `shownSeries` allowlist can also include `CpuTemperature#*`, `CpuPerCore#*`, `CpuClock#*`, `VrmTemperature*`, `RailVoltage*`, `Network*`, and other published instance families. A plain family pattern such as `Gpu*` matches canonical and instance keys. `showFrametimeGraph` controls the frametime sparkline within the FPS group, not a separate sensor category.

## Paths, loading, and saving

`ConfigPaths.ConfigFile` resolves the path independently of the build output:

1. **Development:** walk up from the executable to `PhantomMonitor.slnx` (or `.sln`) and use the repository-root `config.json`.
2. **Installed:** `%AppData%\PhantomMonitor\config.json` on Windows; `$XDG_CONFIG_HOME/PhantomMonitor/config.json` on Linux, usually `~/.config/PhantomMonitor/config.json`.

If the file is missing, `AppConfig.Load` copies `config.example.json` and creates the containing directory. `Normalize()` corrects invalid values, including a minimum 100 ms interval, opacity from 0.1 to 1, and overlay coordinates from −5000 to 5000. `AppConfig.Save()` writes atomically through a temporary file and reports success or failure. Errors are recorded in `logs/errors.log`.

The `showOverlay` option controls the initial overlay toggle state. Settings for CSV, JSON, and JSONL persist in `config.json`. `overlay.showNetwork`, `overlay.showFans`, and `overlay.showMotherboard` are consumed by the overlay and can reveal their rows when the first real reading arrives. `overlay.compactMode`, `overlay.accentColor`, and `overlay.fontSize` control overlay density, accent, and base font. The `sensors.*` category switches are reserved by the model but are not currently consumed by the Linux interface.

## Avalonia settings pages

On Linux, **Settings** can edit the global interval (100–60,000 ms), the three logging toggles, the HTTP exporter, and floating-overlay categories, width, opacity, font, compact mode, and shown-series patterns. A save is applied only after configuration is written successfully. Changing the sensor interval recreates the sensor service and resets session statistics while preserving a stopped or paused state. Overlay appearance and series changes are applied without restarting collection.

The **Custom sensors** and **Per-series tuning** pages edit `customSensors` and `sensorTuning`, respectively. Their order matters: custom formulas can reference earlier entries, and the last matching tuning rule wins. Sensor categories in `sensors.*`, Windows hotkeys, profiles, and advanced OSD templates remain platform-specific or are not consumed by the Linux frontend.

The log directory is displayed read-only in Settings. The telemetry listener stays active while collection is paused and serves the frozen latest snapshot; it closes when collection stops. Binding to all interfaces (`listenAll`) has neither TLS nor authentication. If a configuration save fails, the running options remain in effect.
