> **Language:** English · [Português (Brasil)](../../funções/interface-principal.md)

# Main interface

The unified app (Avalonia, Windows and Linux) uses a dark dashboard with a sidebar and metric cards. Linux-specific behavior is described in [Linux support](linux-support.md).

In the implementation, the main window owns the sidebar, navigation, and global elements; Settings, Custom Sensors, Alerts, and Diagnostics are separate page controls that share the same ViewModel. Overview, Inventory, and Per-series tuning remain in the main XAML.

## Dashboard

The sidebar shows monitor status, uptime, sensor count, page navigation (overview, inventory, custom sensors, series tuning, alerts, settings and diagnostics), overlay visibility, CSV/JSON/JSONL toggles, and the Windows startup option. A system-tray menu provides window visibility, overlay, startup, statistics reset, monitoring pause, OSD profiles, and exit (tray ported from the WPF in F1.5).

Metric cards cover CPU, GPU, FPS, RAM, disk, network, motherboard, and fans. CPU/GPU detail lines include available clocks, power, voltage, VRAM, hotspot, and VRAM temperature. A process list shows applications using the GPU. The in-app diagnostics view displays detected sensors, and a banner can indicate when elevated permissions are needed for some readings.

The cards use restrained horizontal progress bars and outlined badges rather than large gauges. CPU/GPU details appear only for values provided by the hardware; otherwise the interface reports that no matching sensor is available. Network cards show download/upload, and the motherboard/fan card summarizes the highest board temperature and GPU/system fans.

Monitoring starts automatically. The runtime pause control is available from the tray or global hotkey; restarting the app resumes collection.

## Visual language

The interface uses dark neutral surfaces, high-contrast text, royal-blue actions, and restrained progress bars and badges. Usage and temperature indicators change from neutral to amber and coral as values rise. The palette is shared between dashboard components and is separate from the OSD's per-device accents.

The Windows palette uses `#0F1117` for the main background, `#141720` for the sidebar, and `#181B24` for cards with `#232734` borders. Main text is `#F1F5F9`, labels `#94A3B8`, and captions `#64748B`; primary actions use `#2563EB`/`#3B82F6`. Usage below 75% is neutral, 75–90% amber, and at least 90% coral. Temperature badges use green through 65 °C, amber through 80 °C, then coral.

**Theme tokens:** in the unified app, colors and accents live in theme dictionaries (`src/PhantomMonitor.App/Themes/Tokens.*.axaml`, 46 keys mirrored between the two themes) consumed via `{DynamicResource}` (the dynamic ranges use brushes resolved by key in the ViewModel — same catalog). F1.6 completes the two built-in themes: `Neptune` preserves the original Avalonia look and `Slate` ports the retired WPF look verbatim (`Styles.xaml`, banners/pills from the WPF `MainWindow.xaml`, OSD panel `#CC070A0F`/`#25FFFFFF` — files removed with the WPF in Phase 2, provenance documented per line in the dictionary). The choice lives in `config.json` — `"theme": "neptune" | "slate"` (files from the F1.6 era holding `uiTheme` migrate on load), applied at boot and switchable **at runtime** through Settings → INTERFACE (Phase 3: dictionary swap without restart; the CLI `--theme=<name>` overrides the file for comparison/smoke, with legacy `--ui-theme=` still accepted) — see the [visual unification roadmap](../visual-unification-roadmap.md). Also in F1.6: sidebar nav with a selected state (background + 3×14 indicator) via `Nav*` tokens, alert toasts with `AlertToast*` tokens (amber border/header on Slate, like the WPF popup) and a win-x64 publish of the unified binary. Primary action buttons have no token: they follow the platform theme accent (Fluent — on Windows, the OS accent) in both themes.
