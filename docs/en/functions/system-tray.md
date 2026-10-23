> **Language:** English · [Português (Brasil)](../../funções/tray.md)

# System tray and Windows startup

The tray integrates with the Windows notification area using native Win32 `Shell_NotifyIcon`, without WinForms, and the icon is drawn at runtime (a system icon is used as a fallback). In the retired WPF it was drawn through WPF rendering; the unified app uses Avalonia's `TrayIcon` API (section below).

## Tray icon

Left click or double-click restores and activates the main window. Right-click opens a menu with:

- Show Phantom Monitor.
- Toggle the game overlay.
- Enable or disable Windows startup.
- Reset session statistics; active CSV and JSONL receive matching markers.
- Pause or resume monitoring; the last telemetry snapshot remains visible. This state is runtime-only and can also be controlled with `pauseMonitorHotkey` (default `Ctrl+Alt+P`).
- An automatically populated OSD-profile section for automatic per-app selection or a session-only pinned profile.
- Exit.

The icon uses a dark square with a blue border and cyan graph line. Tray failures are logged and do not stop the app; disposal removes the icon and window hook.

The tray uses protocol version 4 (`NIM_SETVERSION`) for modern selection and context-menu events and DPI-aware behavior. Newly created icons may initially be placed in Windows' overflow area; users can pin them in the visible taskbar area. The profile list is rebuilt whenever its menu opens so it reflects current configuration.

## Startup manager

`StartupManager` adds or removes the current executable from `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. It does not require administrator access. The UI compares the stored path with the current executable and reflects the actual registry state, including when the executable has moved. Restricted policies or write failures are reported and logged.

`IsEnabled()` accepts a stored path with or without surrounding quotes and compares it case-insensitively against the current executable path. `SetEnabled(bool)` reports failure when policy prevents a registry update; the UI then rereads the actual state rather than assuming the requested state succeeded.

The sidebar and tray menu both expose the startup toggle.

## Unified app (Avalonia, F1.5)

The unified app ships the same tray through Avalonia's `TrayIcon` API (`src/PhantomMonitor.App/Services/TrayIconService.cs`), with the same menu and the same runtime-drawn icon (dark square with a cyan graph line; no external assets):

- **Windows:** the same `Shell_NotifyIcon` protocol underneath the Avalonia API.
- **Linux:** integration via **StatusNotifier/DBus** — it works on desktops with support (KDE, GNOME with the notification-area extension); on desktops without StatusNotifier the icon simply does not appear and the app stays functional.
- **Menu:** the same items inherited from the WPF (`Show Phantom Monitor`, `Game overlay`, `Start with the system`, `Reset statistics`, `Pause monitoring`, the dynamic OSD-profile section and `Exit`), with checks rebuilt when state changes (`InvalidateMenu`), left-click on the icon activating the window, and the profile item applying the P3 pin (`SetPinnedProfile` — session-only, not persisted to the config).
- `Reset statistics` in the unified app zeroes min/max/average and writes the `#marker reset (inventory)` marker to the active CSV (plus the equivalent note in the active JSONL journal) — the same semantics documented for the inventory window.

Startup is per-OS via `StartupManagerFactory` (Core): `WindowsStartupManager` keeps the HKCU Run entry described above, and on Linux `LinuxStartupManager` writes an XDG autostart entry at `~/.config/autostart/phantom-monitor.desktop` whose `Exec` points to the current executable; `IsEnabled()` validates the entry still matches it. The sidebar and tray menu share the real OS state (`Start with the system`).
