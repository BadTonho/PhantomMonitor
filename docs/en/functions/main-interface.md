> **Language:** English · [Português (Brasil)](../../funções/interface-principal.md)

# Main interface

The unified app (Avalonia, Windows and Linux) uses a dark dashboard with switchable left or bottom navigation and metric cards. Linux-specific behavior is described in [Linux support](linux-support.md).

In the implementation, the main window owns the sidebar, navigation, and global elements; Settings, Custom Sensors, Alerts, and Diagnostics are separate page controls that share the same ViewModel. Overview, Inventory, and Per-series tuning remain in the main XAML.

## Dashboard

In **Settings → INTERFACE**, choose **Left**, **Bottom**, or **Floating dock** navigation and **Icons and text** or **Icons only**. The compact left sidebar is about 76 px wide and uses a “P” mark; all links retain tooltips, accessible names, and the selected-page indicator. Bottom mode hides the sidebar and places links in a horizontally scrollable footer reserved by the window. The dock is a fixed capsule centered over the page near the bottom, inside the main window, with no separate window or drag behavior; extra scroll space keeps it clear of the last controls. Its combined icon reports monitoring and update status and opens Settings when an update notice is available. These choices save immediately; older configs use the left layout with labels. The startup option is in Settings. Monitoring controls are available in the page headers; the system-tray menu provides window visibility, overlay, startup, statistics reset, monitoring pause, OSD profiles, and exit.

Metric cards cover CPU, GPU, FPS, RAM, disk, network, motherboard, and fans. CPU/GPU detail lines include available clocks, power, voltage, VRAM, hotspot, and VRAM temperature. A process list shows applications using the GPU. The in-app diagnostics view displays detected sensors, and a banner can indicate when elevated permissions are needed for some readings. When the user chooses to restart as administrator, the elevated process waits for the current instance to release the single-instance lock before opening its window.

The cards use history charts and restrained temperature badges. CPU/GPU details appear only for values provided by the hardware; otherwise the interface reports that no matching sensor is available. Network cards show download/upload, and the motherboard/fan card summarizes the highest board temperature and GPU/system fans.

Monitoring starts automatically. The runtime pause control is available from the tray or global hotkey; restarting the app resumes collection.

The sensor inventory has compact expandable category headers, indented sensor instances, and aligned current, minimum, maximum, and average columns. Tooltips expose long names and values; the table and monitor list scroll horizontally in narrow windows. Colors follow the selected theme and custom palette.


Desktop windows share styles for actions, inputs, and secondary text. The four smaller dashboard cards use two columns below 760 logical units of available width. When FPS is unavailable, the chart and statistics are hidden until a reading arrives. Missing board temperature is shown as unavailable; a zero reading remains valid. The in-game overlay windows do not include the desktop styles.

## Visual language

The interface uses dark neutral surfaces, legible secondary text, blue primary actions, and restrained charts and badges. Usage and temperature indicators change from neutral to amber and coral as values rise. The palette is shared between dashboard components and is separate from the OSD's per-device accents.

The Windows palette uses `#0F1117` for the main background, `#141720` for the sidebar, and `#181B24` for cards with `#232734` borders. Main text is `#F1F5F9`, labels and secondary captions `#94A3B8`; primary actions use `#2563EB`/`#3B82F6`. Usage below 75% is neutral, 75–90% amber, and at least 90% coral. Temperature badges use green through 65 °C, amber through 80 °C, then coral.

**Theme tokens:** in the unified app, colors and accents live in theme dictionaries (`src/PhantomMonitor.App/Themes/Tokens.*.axaml`, 49 keys mirrored between the two themes) consumed via `{DynamicResource}` (the dynamic ranges use brushes resolved by key in the ViewModel — same catalog). F1.6 completes the two built-in themes: `Neptune` derives from the original Avalonia palette and `Slate` from the retired WPF palette (`Styles.xaml`, banners/pills from the WPF `MainWindow.xaml`, OSD panel `#CC070A0F`/`#25FFFFFF` — files removed with the WPF in Phase 2, provenance documented per line in the dictionary). The choice lives in `config.json` — `"theme": "neptune" | "slate"` (files from the F1.6 era holding `uiTheme` migrate on load), applied at boot and switchable **at runtime** through Settings → INTERFACE (Phase 3: dictionary swap without restart; the CLI `--theme=<name>` overrides the file for comparison/smoke, with legacy `--ui-theme=` still accepted) — see the [visual unification roadmap](../visual-unification-roadmap.md). Also in F1.6: sidebar nav with a selected state (background + 3×14 indicator) via `Nav*` tokens, alert toasts with `AlertToast*` tokens (amber border/header on Slate, like the WPF popup) and a win-x64 publish of the unified binary.

The INTERFACE card also opens a palette editor with five family base colors and an advanced view for all 49 theme colors. Neptune and Slate keep separate palettes. Individual overrides take precedence over family colors and survive family edits; resetting an individual token returns it to its calculated family color. **Done** keeps a draft, and **Save and apply** persists and applies it only after the config save succeeds. The primary accent also controls action buttons and the selected navigation indicator, independent of the Windows accent.
