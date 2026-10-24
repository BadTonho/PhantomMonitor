> **Language:** English · [Português (Brasil)](../../funções/overlay.md)

# Overlay window

## Floating overlay in Avalonia/Linux

The Avalonia dashboard offers a floating overlay controlled by `showOverlay` and the **Show/Hide overlay** button. Each category is a separate transparent window, so freely placed blocks do not create a full-screen native window that captures clicks outside the metrics. If a block is closed by the system, the whole overlay is disabled and can be reopened from the dashboard.

Under **Settings → Floating overlay**, **Open overlay editor** opens a live preview on a gridded canvas. Right-click an empty area to create a block; left-drag to move it with 20 px grid snapping and edge alignment to nearby blocks (or freely while holding Shift), constrained by the rendered block size and canvas edges. Double-click a block to organize and customize its content; use **Add content** at the bottom of that window to open the chooser for categories, individual series or patterns, graphs, and templates. Right-click a block to edit or remove it. Graphs and templates can be created and edited in the add-content picker. **Settings**, at the editor's lower-right corner, opens a panel on the right with a left-to-right slide animation; the window expands to make room without covering or shrinking the canvas. The panel contains width, overall opacity, background color and opacity, font family and size, compact mode, and border. Toggle the border and choose a color from the palette or enter HEX; clearing the color restores the theme border. Overall opacity affects the whole block; background opacity combines with it and changes only the fill. Choose a color from the palette or enter HEX; clearing the color restores the theme. Choose an installed font or type a family name; leaving it blank preserves the current typography. Changes remain a draft: **Save and apply** writes them and updates the running overlay; closing the editor discards unsaved appearance, content, and position changes.

Under **Block order**, drag an item to reposition it in the list; the move-up and move-down buttons remain available. The selected row's editor appears to the right of the list. Use **Add content** below the columns to open the chooser for categories, series, graphs, and templates. Confirming that window updates the list draft; canceling discards its changes. Categories remain groups. Select a category or series and choose a row to edit its name and color; individual series also support automatic precision or 0 to 3 decimal places. Changes reach the block only after confirming **Apply content**.
Positions in `overlay.blocks` are normalized from 0 to 1 relative to the monitored window on Windows or the available screen area on Linux. Configurations without `blocks` are migrated while preserving enabled categories, per-instance series, graphs, templates, and existing positions; when no positions exist, blocks start in a vertical column. New installations also start with the current default blocks. Width (140–700 px), overall opacity (10–100%), background color (#RRGGBB), background opacity (0–100%), font family and size (8–20 pt), compact mode, border visibility, and border color are global settings on **Settings → Floating overlay** and in the editor's settings panel. Editor changes are persisted when **Save and apply** is clicked.

The **Frametime graph** item can be combined with other content in any block. The gold sparkline appears once at least two samples exist, is 24 px high normally and 18 px in compact mode, and stops advancing while paused.

### Per-instance series

In a block's content selector, filter detected series by CPU, GPU, RAM, Disk, Network, Fans, Motherboard, Battery, FPS, or Other; **All** shows every series. **Select all visible series** selects or clears the current filter's series in bulk and indicates a partial selection. This affects individual series only and does not enable the category summary shortcut. You can also add a key or pattern such as `Gpu*`, `*Total`, or `CpuTemperature#*`; unmatched patterns stay in the block and begin displaying data when the series appears. Existing selections and manual patterns are preserved when changing the filter. The legacy `overlay.shownSeries` setting is preserved for compatibility and migrates to series items in blocks on the first save. Series are ordered by type and numeric index, include the key and hardware label, display zero when a reading is present, and are limited to 24 rows per block.

Blocks can contain any combination of categories, series, graphs, and templates. The preview shows live values; each block's content and position are independent. Active per-application profiles can still override width, font size, overall opacity, and accent, and filter per-instance series. Background color and opacity, border, and font family remain global.

Matching per-instance rows are ordered by family, show the key, hardware label, and formatted value, and are limited to 24. A present zero is displayed as zero. Canonical category rows are not duplicated. On **Windows**, each block is click-through (`WS_EX_TRANSPARENT`) and follows the focused app every 30 ms; `overlayPosition` and profiles offset the group. On **Linux**, each block is an individual Topmost window; the compositor controls its initial position and whether it appears over fullscreen applications.

The unified app offers templates, per-instance series, application profiles, graphs, Windows click-through, and per-profile accents. Blocks, series, graphs, and templates can be composed in the current visual editor; per-application profiles remain in `config.json`.

Internally, positioning dispatch is separate from the Windows and Linux implementations (`OverlayWindow.Positioning.cs`, `OverlayWindow.Windows.cs`, and `OverlayWindow.Linux.cs`). This keeps Windows focus tracking and click-through rules separate from compositor-limited Linux placement.

The content editor and add-content picker are dedicated AXAML windows with state and editing operations in `OverlayBlockContentDialogViewModel`; their Views forward control actions. The picker opens an isolated copy of the draft: **Add content** confirms its choices into the main list, while **Cancel** discards them. Graphs, templates, and multi-selection remain a draft until the user applies the main window and then saves the layout.

## Game OSD layout (inherited from the WPF)

The Windows OSD is a transparent, click-through, topmost window designed to stay compact over games. It uses one aligned row per device, monospaced text, device-specific accents, and automatic color changes for high temperature or utilization.

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

CPU and GPU can show compact secondary details such as clocks, power, voltage, VRAM, hotspot, and VRAM temperature. Network, fan, and motherboard rows are revealed when real readings first appear. Per-instance rows are selected by block contents (an active profile can apply its `shownSeries` allowlist), grouped by family, and limited to 24 per block. Missing instances are hidden until they return. The FPS section can include average, 1%/0.1% lows, maximum, and a slim frametime chart.

Typical instance tags are `GPU1`/`GPU2...` for multi-GPU metrics; `FAN1...` for fan slots; `DSK1...` for disk activity, throughput, temperature, and health; `CPU1...` for per-core temperature, usage, and clock; `MB1...`, `VRM1...`, and `RL1...` for board temperatures, VRM, and voltage rails; and `NET1...` for interface rates and session totals. Per-core rows are disabled by default because they can crowd the display on CPUs with many cores. If a device disappears, its instance row is hidden until the hardware returns.

The device accents are orange for GPU, cyan for CPU, purple for RAM, blue for disks, green for network, pink for fans, light gray for motherboard, and bright green for FPS. Usage and temperature colors change with load. CPU detail can show clock, power, and voltage; GPU detail can show VRAM used/total, core and memory clocks, power, voltage, hotspot, and memory temperature. These detail rows stay hidden when no values are available.

## Visual overlay editor

The editor is a gridded canvas with a live preview. Right-click an empty area to create a block; left-drag to move it with 20 px grid snapping and edge alignment to nearby blocks (or freely while holding Shift), constrained by the rendered block size and canvas edges. Double-click a block to edit its order and sensor rows; use **Add content** in the block dialog to open the multi-item selector. Right-click a block to edit or remove it. Normalized `x`/`y` positions range from 0 to 1 and use the monitored window as reference on Windows and the available screen area on Linux. Content and positions stay as a draft until **Save and apply**. Older configurations migrate from `layout` and other legacy options into `blocks`; without saved positions, blocks start in the default vertical order.

### Configuration

Current block composition is stored in `overlay.blocks`; global appearance is edited on **Settings → Floating overlay**. The legacy fields remain for compatibility and migration:

- `width`, `fontSize`, `fontFamily`, `compactMode`, `accentColor`, and global `overlayOpacity`.
- `blocks`: `{ id, x, y, items: [{ kind, value }] }`; `kind` is `category`, `series`, `graph`, `template`, or `frametime`. A block can contain multiple items.
- `overlay.blocks[].metricOverrides`: per-block sensor presentation overrides with `identity`, `displayName`, `decimals` (0–3), and `color`.
- `showCpu`, `showGpu`, `showRam`, `showDisk`, `showNetwork`, `showFans`, `showMotherboard`, `showFps`, `showFrametimeGraph`, `shownSeries`, and `layout` are read when migrating an older config without `blocks`.
- `backgroundColor`: `#RRGGBB`; `null` follows the active theme.
- `backgroundOpacity`: additional fill opacity from 0 to 1 (default 1).
- `borderEnabled`: border visibility (default `true`).
- `borderColor`: `#RRGGBB`; `null` uses the theme/accent border color.
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

`visible: false` hides the OSD while the profile matches. A tray menu can pin a profile for the current session or return to automatic matching. When a profile is active, a small footer indicator names it. Profile fields remain in `config.json`; the Avalonia app consumes `profiles` on the focused-process tick on Windows.

### Custom text templates

`overlay.templates` appends custom text rows after per-instance series:

```json
"templates": [
  { "text": "VRAM {GpuMemoryUsed:N0}/{GpuMemoryTotal:N0} MB", "color": "#A3E635" },
  { "text": "Fan: {FanSpeed*} RPM" }
]
```

`{Key}` resolves canonical or instance data; wildcard placeholders select the first match. `{Key:N}` chooses 0–3 decimals (default 1). Literal text supplies units. A row stays hidden until all placeholders resolve. Font follows the OSD at base size minus one point. The visual editor can create and edit templates; changes apply after **Save and apply**.

### Per-series history graphs

`overlay.graphs` defines sparklines before the template block:

```json
"graphs": [
  { "series": "GpuTemperature" },
  { "series": "Custom#1", "height": 48, "window": 600, "color": "#A3E635" },
  { "series": "CpuCoreClock#*", "min": 2000, "max": 5500 }
]
```

`series` accepts canonical, instance, wildcard, or custom-sensor name. `height` is 12–120 px (default 36); `window` is 30–1200 samples (default 240); `color` defaults to the OSD accent; `min`/`max` optionally fix the Y axis. Missing series hide the graph while preserving its circular history. Up to 12 graphs are retained. Graphs can consume any published series, including custom formulas and PresentMon metrics. The visual editor can create and edit graphs; changes apply after **Save and apply**. Each entry has its own persistent circular history and optional fixed axis.

The Windows implementation uses layered, transparent, topmost window styles (`WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, and `HWND_TOPMOST`); the click-through behavior comes from these Windows styles.

Profile `opacity` overrides the global `overlayOpacity` and is normalized to 0.1–1. Use `visible: false` to hide the OSD while a profile matches. The tray profile pin overrides automatic matching for the current app session and does not write back to configuration. When a profile is active, the OSD shows a small `profile: <name>` footer.

For templates, a placeholder without an explicit series can use the canonical value or the first available instance. A wildcard placeholder resolves to the first matching `SensorKey`. A template line is placed at the bottom of the panel, after the dynamic instance-series block. Template font size is the base OSD size minus one point.

For history graphs, a flat series receives a small automatic vertical range (±0.5) so it remains visible. Automatic-axis graphs use the current window's minimum and maximum; explicit `min`/`max` values give a stable benchmark scale. Missing series hide the graph without clearing its circular history. At most 12 graphs are retained by normalization.
