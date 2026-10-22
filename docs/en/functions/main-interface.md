> **Language:** English · [Português (Brasil)](../../funções/interface-principal.md)

# Main interface

The Windows WPF frontend uses a dark dashboard with a sidebar and metric cards. The Avalonia Linux dashboard and its pages are described in [Linux support](linux-support.md).

## Windows dashboard

The sidebar shows monitor status, uptime, sensor count, navigation, overlay visibility, shortcuts to overlay and hardware inventory windows, CSV/JSON/JSONL toggles, and the Windows startup option. A system-tray menu provides window visibility, overlay, startup, statistics reset, monitoring pause, OSD profiles, and exit.

Metric cards cover CPU, GPU, FPS, RAM, disk, network, motherboard, and fans. CPU/GPU detail lines include available clocks, power, voltage, VRAM, hotspot, and VRAM temperature. A process list shows applications using the GPU. The diagnostic window displays detected sensors, and a banner can indicate when elevated permissions are needed for some readings.

The cards use restrained horizontal progress bars and outlined badges rather than large gauges. CPU/GPU details appear only for values provided by the hardware; otherwise the interface reports that no matching sensor is available. Network cards show download/upload, and the motherboard/fan card summarizes the highest board temperature and GPU/system fans.

Monitoring starts automatically. The runtime pause control is available from the tray or global hotkey; restarting the app resumes collection.

## Visual language

The interface uses dark neutral surfaces, high-contrast text, royal-blue actions, and restrained progress bars and badges. Usage and temperature indicators change from neutral to amber and coral as values rise. The palette is shared between dashboard components and is separate from the OSD's per-device accents.

The Windows palette uses `#0F1117` for the main background, `#141720` for the sidebar, and `#181B24` for cards with `#232734` borders. Main text is `#F1F5F9`, labels `#94A3B8`, and captions `#64748B`; primary actions use `#2563EB`/`#3B82F6`. Usage below 75% is neutral, 75–90% amber, and at least 90% coral. Temperature badges use green through 65 °C, amber through 80 °C, then coral.
