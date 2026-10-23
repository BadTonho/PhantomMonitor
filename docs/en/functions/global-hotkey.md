> **Language:** English · [Português (Brasil)](../../funções/hotkey.md)

# Global hotkeys (`HotkeyService` / `HotkeyParser`)

The unified app registers global hotkeys with Win32 `RegisterHotKey` on Windows, so the overlay can be toggled from another application without injecting a DLL or installing a keyboard hook. The hotkey message is delivered through a message-only window via `WM_HOTKEY` (Linux does not register global hotkeys yet; X11/XWayland is the future path).

## Supported syntax

`HotkeyParser` reads the string from `config.json`. Modifiers are `Ctrl`/`Control`, `Alt`, `Shift`, and `Win`/`Windows`, separated by `+` or spaces. Keys can be `A`–`Z`, `0`–`9`, or `F1`–`F24`. A combination must include a modifier or a function key to prevent capturing a bare letter or digit globally.

Examples: `Ctrl+Shift+O`, `Ctrl+Alt+F10`, `F9`, `Win+Shift+P`.

## Configuration and lifecycle

- `toggleOverlayHotkey` defaults to an empty string (disabled) and toggles OSD visibility.
- `pauseMonitorHotkey` defaults to `Ctrl+Alt+P` and pauses/resumes sensor collection. Paused output and overlays retain the last snapshot; the state is runtime-only and does not persist across restarts. Empty disables it.
- Each hotkey is registered by `GlobalHotkeyService` (message-only `WM_HOTKEY` window). Invalid or already-claimed combinations fail and are logged to `logs/errors.log`.
- `GlobalHotkeyService` uses separate Win32 hotkey IDs for overlay (`0x5048`) and pause (`0x5049`) — the same IDs the retired WPF used. Registration is updated when settings are applied and removed when the app closes.

## Unified app (Windows, F1.5)

The Avalonia frontend does not expose a WndProc for the main window, so the behavior is provided by `GlobalHotkeyService` in `Core/Services/Windows/`: a **message-only** window on a dedicated thread is created with `HWND_MESSAGE` (`-3`), receives `WM_HOTKEY`, and publishes each callback to the UI dispatcher (the retired WPF used the main window's `HwndSource`). It reuses `HotkeyParser`, the same default/pause hotkey IDs as the WPF (`0x5048`/`0x5049`), `MOD_NOREPEAT`, and re-registers a changed combination with the same ID on reconfiguration. Failures (invalid spec, combination already in use by another application) are logged to `logs/errors.log` without stopping the app.

These hotkeys are Windows-specific; the unified app does not register them on Linux yet (X11/XWayland is the future path; pure Wayland does not allow global hotkeys by design).
