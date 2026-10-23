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
| `theme` | Built-in UI theme of the unified app: `"neptune"` (original Avalonia look) or `"slate"` (verbatim port of the WPF/Windows look), applied at boot and switchable **at runtime** through Settings → INTERFACE (Phase 3 — no restart); files from the F1.6 era holding `uiTheme` migrate to the new field on load (the file now only writes `theme`). Unknown values fall back to `neptune`, and the CLI `--theme=<name>` overrides the file (handy to compare themes without editing the config; the legacy `--ui-theme=` spelling is still accepted). See [main interface](main-interface.md). |
| `overlayOpacity`, `overlayPosition`, `overlay` | Global opacity, initial position, and overlay settings. |
| `sensors` | Sensor categories enabled by the model; reserved switches the interface does not fully consume. |
| `alerts` | Alert rules and actions. The unified app edits rules, cooldowns, internal notifications, and scripts; on Linux the notification carries the sound and shutdown is not executed, while on Windows (since F1.5) the app runs all actions at the former WPF parity. See [alerts](alerts.md). |
| `telemetryExport` | Optional HTTP exporter: `{ "enabled": false, "port": 7317, "listenAll": false }`; provides `/values` and `/health`. See the [Telemetry SDK](../telemetry-sdk.md). |
| `update` | Update checks via GitHub Releases: `{ "autoCheck": true, "checkBeta": false, "repository": "" }` — automatic check at startup and every 24 h (toggle in Settings) plus a manual button; `checkBeta` accepts prereleases ("beta channel"); an empty `repository` falls back to the official one (`BadTonho/PhantomMonitor`; a GitHub URL is also accepted). Shared implementation in `PhantomMonitor.Services.Update` (Core). |
| `profiles` | Windows OSD per-application overrides: name, process match, position, width, font, opacity, accent, series, and visibility. See [overlay](overlay.md). |
| `overlay.blocks` | Custom overlay blocks: `{ id, x, y, items: [{ kind, value }] }`. Positions are normalized from 0 to 1; `kind` is `category`, `series`, `graph`, `template`, or `frametime`. A block can contain multiple items. |
| `overlay.templates`, `overlay.graphs` | Custom OSD text lines and per-series history sparklines; these definitions can be edited in the block content selector. |
| `customSensors` | User formulas in the form `{ name, formula, unit? }`, published as `Custom#i`. Avalonia has an editor. See [custom sensors](custom-sensors.md). |
| `sensorTuning` | Per-series publication interval and smoothing rules. See [per-series tuning](series-tuning.md). |

## `OverlayConfig`

`width`, `overlayOpacity`, `fontSize`, and `compactMode` control global appearance. `blocks` stores stable block ids, ordered content items, and normalized positions. The legacy `show*`, `shownSeries`, and `layout` fields are read when migrating configurations that do not yet have `blocks`; after migration, block content and positions come from `blocks`.

Series items can use keys or patterns such as `Gpu*`, `CpuTemperature#*`, `VrmTemperature*`, `RailVoltage*`, and `Network*`. Graph and template item values reference their stable ids in `overlay.graphs` and `overlay.templates`. The frametime item can appear in any block.

## Paths, loading, and saving

`ConfigPaths.ConfigFile` resolves the path independently of the build output:

1. **Development:** walk up from the executable to `PhantomMonitor.slnx` (or `.sln`) and use the repository-root `config.json`.
2. **Installed:** `%AppData%\PhantomMonitor\config.json` on Windows; `$XDG_CONFIG_HOME/PhantomMonitor/config.json` on Linux, usually `~/.config/PhantomMonitor/config.json`.

If the file is missing, `AppConfig.Load` copies `config.example.json` and creates the containing directory. `Normalize()` corrects invalid values, including a minimum 100 ms interval, opacity from 0.1 to 1, and overlay coordinates from −5000 to 5000. `AppConfig.Save()` writes atomically through a temporary file and reports success or failure. Errors are recorded in `logs/errors.log`.

The `showOverlay` option controls the initial overlay toggle state. Settings for CSV, JSON, and JSONL persist in `config.json`. Legacy `overlay.show*`, `shownSeries`, and `layout` values are used when migrating to `overlay.blocks`. `overlay.compactMode`, `overlay.accentColor`, and `overlay.fontSize` control overlay density, accent, and base font. The `sensors.*` category switches are reserved by the model but are not currently consumed by the interface.

## Settings pages

In the unified app, **Settings** can edit the global interval (100–60,000 ms), logging toggles, theme, HTTP exporter, and global floating-overlay appearance. The overlay editor is a gridded live canvas: right-click an empty area to add a block, drag to move it, double-click to select content, and right-click a block to edit or remove it. Changes remain a draft until **Save and apply** succeeds. Changing the sensor interval recreates the sensor service and resets session statistics while preserving a stopped or paused state. Overlay appearance and layout changes apply without restarting collection.

When loading an older config without `overlay.blocks`, normalization migrates enabled categories, `shownSeries`, graphs, templates, and positions from `layout`. If no positions were saved, it creates the previous vertical order. The next successful save persists the migrated `blocks`; a new installation starts with the current default blocks. Width, opacity, font size, and compact mode are edited on the **Settings → Floating overlay** page.

The **Custom sensors** and **Per-series tuning** pages edit `customSensors` and `sensorTuning`, respectively. Their order matters: custom formulas can reference earlier entries, and the last matching tuning rule wins. Sensor categories in `sensors.*` and per-application profiles remain in `config.json`; Windows hotkeys are Windows-only.

The log directory is displayed read-only in Settings. The telemetry listener stays active while collection is paused and serves the frozen latest snapshot; it closes when collection stops. Binding to all interfaces (`listenAll`) has neither TLS nor authentication. If a configuration save fails, the running options remain in effect.

Internally, `AppConfig` remains the read/write boundary. Normalization and overlay migration live in dedicated partials; configuration models are grouped into overlay, sensor, and other option files without changing JSON names or existing defaults.
