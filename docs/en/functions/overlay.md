> **Language:** English · [Português (Brasil)](../../funções/overlay.md)

# Overlay window

## Floating overlay in Avalonia/Linux

The Avalonia dashboard offers a separate floating window controlled by `showOverlay` and the **Show/Hide overlay** button. The window is borderless, interactive, always on top of ordinary windows, and can be dragged by its header. It follows sensor snapshots; unavailable readings display `—`, and pausing collection freezes the last values.

Under **Settings → Floating overlay**, choose categories, width (140–700 px), opacity (10–100%), font size (8–20 pt in 0.5 pt steps), and compact mode. Appearance changes apply after saving and do not restart collection. The frametime chart option uses `overlay.showFrametimeGraph`; it appears below the FPS row once at least two samples exist, using a gold line, 24 px high normally and 18 px in compact mode. The chart stops advancing while paused.

### Per-instance series

The settings page lets you add detected series or enter keys/patterns manually for `overlay.shownSeries`. The list is a whitelist: a series appears when any saved pattern matches. Supported examples include `Gpu*`, `*Total`, and `CpuTemperature#*`. Unmatched patterns remain saved for future hardware. Remove patterns individually or restore the defaults `Gpu*`, `FanSpeed*`, and `Disk*`. An empty list cannot be saved because configuration normalization restores the defaults.

Matching per-instance rows are ordered by family, show the key, hardware label, and formatted value, and are limited to 24. A present zero is displayed as zero. Canonical category rows are not duplicated. On Wayland, the compositor controls the initial position and whether the window appears over fullscreen applications; transparency also depends on compositor support. Click-through is not available in this Avalonia window.

The Linux overlay does not currently include application profiles, custom templates, per-series graphs, or the WPF visual editor.

## WPF game OSD

The Windows WPF OSD is a transparent, click-through, topmost window designed to stay compact over games. It uses one aligned row per device, monospaced text, device-specific accents, and automatic color changes for high temperature or utilization.

| Tag | Display |
|---|---|
| `GPU` | Usage and temperature, orange |
| `CPU` | Usage and temperature, cyan |
| `RAM` | Used memory and percentage, purple |
| `DSK` | Disk read/write rates, blue |
| `NET` | Download and upload, green |
| `FAN` | GPU and system fans, pink |
| `MB` | Highest motherboard/SuperIO temperature, gray |
| `FPS` | FPS and frametime, bright green |

CPU and GPU can show compact secondary details such as clocks, power, voltage, VRAM, hotspot, and VRAM temperature. Network, fan, and motherboard rows are revealed when real readings first appear. Per-instance rows are filtered by `overlay.shownSeries`, grouped by family (GPU, fans, disks, RAM, CPU, network, battery, then other families), and limited to 24. Missing instances are hidden until they return. The FPS section can include average, 1%/0.1% lows, maximum, and a slim frametime chart.

Typical instance tags are `GPU1`/`GPU2...` for multi-GPU metrics; `FAN1...` for fan slots; `DSK1...` for disk activity, throughput, temperature, and health; `CPU1...` for per-core temperature, usage, and clock; `MB1...`, `VRM1...`, and `RL1...` for board temperatures, VRM, and voltage rails; and `NET1...` for interface rates and session totals. Per-core rows are disabled by default in the WPF OSD because they can crowd the display on CPUs with many cores. If a device disappears, its instance row is hidden until the hardware returns.

The device accents are orange for GPU, cyan for CPU, purple for RAM, blue for disks, green for network, pink for fans, light gray for motherboard, and bright green for FPS. Usage and temperature colors change with load. CPU detail can show clock, power, and voltage; GPU detail can show VRAM used/total, core and memory clocks, power, voltage, hotspot, and memory temperature. These detail rows stay hidden when no values are available.

## WPF visual editor

`OverlayEditorWindow` provides a dark grid canvas for moving metric blocks, snapping to a 10 px grid, selecting layers, editing X/Y offsets, and toggling each category. Its inspector controls width, opacity, compact mode, font (8–20 pt), accent color, per-instance patterns, and global hotkey. **Apply & Save** writes `config.json` and updates the running overlay.

Dragging a block also changes the overlay's global offset, and the X/Y values track that movement. Quick-position presets cover the four screen corners. The per-instance section exposes family checkboxes for multiple GPUs, extra fans, disks, CPU cores, CPU power domains, motherboard sensors, VRM, voltage rails, network interfaces, and RAM temperatures. Manually configured patterns that have no checkbox remain intact when the editor saves. The editor also manages application profiles and multiline template text; template colors stay in configuration and are preserved for unchanged text.

### Configuration

Core `overlay` fields include:

- `width`, `fontSize`, `compactMode`, `accentColor`, and global `overlayOpacity`.
- `showCpu`, `showGpu`, `showRam`, `showDisk`, `showNetwork`, `showFans`, `showMotherboard`, `showFps`, and `showFrametimeGraph`.
- `shownSeries`: a `SensorKey` pattern allowlist; defaults to `["Gpu*", "FanSpeed*", "Disk*"]`.
- `overlayPosition`: screen coordinates.
- `toggleOverlayHotkey` and `pauseMonitorHotkey` on Windows.

### Per-application profiles (Windows)

`profiles` contains optional overrides for a focused executable. Matching is case-insensitive by executable name/prefix; empty or `*` matches all apps. Null fields inherit global settings.

```json
"profiles": [
  { "name": "CS2", "appMatch": "cs2", "offsetX": 30, "offsetY": 120,
    "width": 260, "fontSize": 13, "opacity": 0.9,
    "series": ["Gpu*", "FanSpeed*", "CpuTemperature#*"],
    "accentColor": "#FFCC15", "visible": true },
  { "name": "Hide in game X", "appMatch": "example", "visible": false }
]
```

`visible: false` hides the OSD while the profile matches. A tray menu can pin a profile for the current session or return to automatic matching. When a profile is active, a small footer indicator names it. The visual editor manages profile fields and series patterns.

### Custom text templates (Windows)

`overlay.templates` appends custom text rows after per-instance series:

```json
"templates": [
  { "text": "VRAM {GpuMemoryUsed:N0}/{GpuMemoryTotal:N0} MB", "color": "#A3E635" },
  { "text": "Fan: {FanSpeed*} RPM" }
]
```

`{Key}` resolves canonical or instance data; wildcard placeholders select the first match. `{Key:N}` chooses 0–3 decimals (default 1). Literal text supplies units. A row stays hidden until all placeholders resolve. Font follows the OSD at base size minus one point. Color is configured in JSON; the editor changes text while preserving the color for unchanged rows.

### Per-series history graphs (Windows)

`overlay.graphs` defines sparklines before the template block:

```json
"graphs": [
  { "series": "GpuTemperature" },
  { "series": "Custom#1", "height": 48, "window": 600, "color": "#A3E635" },
  { "series": "CpuCoreClock#*", "min": 2000, "max": 5500 }
]
```

`series` accepts canonical, instance, wildcard, or custom-sensor name. `height` is 12–120 px (default 36); `window` is 30–1200 samples (default 240); `color` defaults to the OSD accent; `min`/`max` optionally fix the Y axis. Missing series hide the graph while preserving its circular history. Up to 12 graphs are retained. Graphs can consume any published series, including custom formulas and PresentMon metrics. They are configured in JSON; applying the visual editor recreates the overlay so new graphs appear.

The Windows API uses layered, transparent, topmost window styles (`WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, and `HWND_TOPMOST`). This click-through behavior is specific to the WPF implementation.

Profile `opacity` overrides the global `overlayOpacity` and is normalized to 0.1–1. Use `visible: false` to hide the OSD while a profile matches. The tray profile pin overrides automatic matching for the current app session and does not write back to configuration. When a profile is active, the OSD shows a small `profile: <name>` footer.

For templates, a placeholder without an explicit series can use the canonical value or the first available instance. A wildcard placeholder resolves to the first matching `SensorKey`. A template line is placed at the bottom of the panel, after the dynamic instance-series block. Template font size is the base OSD size minus one point.

For history graphs, a flat series receives a small automatic vertical range (±0.5) so it remains visible. Automatic-axis graphs use the current window's minimum and maximum; explicit `min`/`max` values give a stable benchmark scale. Missing series hide the graph without clearing its circular history. At most 12 graphs are retained by normalization.
