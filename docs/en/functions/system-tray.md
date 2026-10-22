> **Language:** English · [Português (Brasil)](../../funções/tray.md)

# System tray and Windows startup

The WPF frontend integrates with the Windows notification area using native Win32 `Shell_NotifyIcon`, without WinForms. The tray icon is drawn at runtime with WPF; a system icon is used as a fallback.

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

The sidebar and tray menu both expose the startup toggle. This feature is Windows-only; the Linux Avalonia frontend has no system tray or startup integration at this time.
