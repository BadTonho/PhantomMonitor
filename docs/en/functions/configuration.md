> **Language:** English · [Português (Brasil)](../../funções/configuracao.md)

# App configuration

Phantom Monitor uses JSON configuration. In a development checkout, `config.json` is created in the repository root; installed builds use a per-user location.

## Main settings

| Setting | Purpose |
|---|---|
| `updateIntervalMs` | Global sampling interval in milliseconds (default 1000; minimum 100). |
| `logToCsv`, `logToJson`, `logToJsonJournal` | Enable CSV, latest-snapshot JSON, or append-only JSONL logging. See [logging](logging.md). |
| `showOverlay` | Show the overlay at startup; Avalonia also uses it to control the floating Linux overlay from the dashboard. |
| `toggleOverlayHotkey`, `pauseMonitorHotkey` | Global Windows hotkeys (RegisterHotKey) for overlay visibility and monitoring pause/resume — honored by the unified app on Windows (formerly by the retired WPF frontend, since F1.5). See [global hotkeys](global-hotkey.md). |
| `uiTheme` | Built-in UI theme of the unified app: `"neptune"` (original Linux look) or `"slate"` (verbatim port of the WPF/Windows look), applied at boot (runtime switching by UI arrives in phase 3 of the visual unification); unknown values fall back to `neptune`, and the CLI `--ui-theme=<name>` overrides the file (handy to compare themes without editing the config). See [main interface](main-interface.md). |
| `overlayOpacity`, `overlayPosition`, `overlay` | Global opacity, initial position, and overlay settings. |
| `sensors` | Sensor categories enabled by the model; reserved switches the interface does not fully consume. |
| `alerts` | Alert rules and actions. The unified app edits rules, cooldowns, internal notifications, and scripts; on Linux the notification carries the sound and shutdown is not executed, while on Windows (since F1.5) the app runs all actions at the former WPF parity. See [alerts](alerts.md). |
| `telemetryExport` | Optional HTTP exporter: `{ "enabled": false, "port": 7317, "listenAll": false }`; provides `/values` and `/health`. See the [Telemetry SDK](../telemetry-sdk.md). |
| `update` | Update checks via GitHub Releases: `{ "autoCheck": true, "checkBeta": false, "repository": "" }` — automatic check at startup and every 24 h (toggle in Settings) plus a manual button; `checkBeta` accepts prereleases ("beta channel"); an empty `repository` falls back to the official one (`BadTonho/PhantomMonitor`; a GitHub URL is also accepted). Shared implementation in `PhantomMonitor.Services.Update` (Core). |
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

The **Custom sensors** and **Per-series tuning** pages edit `customSensors` and `sensorTuning`, respectively. Their order matters: custom formulas can reference earlier entries, and the last matching tuning rule wins. Sensor categories in `sensors.*`, profiles, and advanced OSD templates are edited via `config.json` (the WPF visual editor was retired in Phase 2); Windows hotkeys are Windows-only.

The log directory is displayed read-only in Settings. The telemetry listener stays active while collection is paused and serves the frozen latest snapshot; it closes when collection stops. Binding to all interfaces (`listenAll`) has neither TLS nor authentication. If a configuration save fails, the running options remain in effect.
