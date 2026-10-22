> **Language:** English · [Português (Brasil)](../../funções/hotkey.md)

# Global hotkeys (`HotkeyService` / `HotkeyParser`)

The Windows frontend registers global hotkeys with Win32 `RegisterHotKey`, so the overlay can be toggled from another application without injecting a DLL or installing a keyboard hook. The hotkey message is delivered to the main window through `WM_HOTKEY`.

## Supported syntax

`HotkeyParser` reads the string from `config.json`. Modifiers are `Ctrl`/`Control`, `Alt`, `Shift`, and `Win`/`Windows`, separated by `+` or spaces. Keys can be `A`–`Z`, `0`–`9`, or `F1`–`F24`. A combination must include a modifier or a function key to prevent capturing a bare letter or digit globally.

Examples: `Ctrl+Shift+O`, `Ctrl+Alt+F10`, `F9`, `Win+Shift+P`.

## Configuration and lifecycle

- `toggleOverlayHotkey` defaults to an empty string (disabled) and toggles OSD visibility.
- `pauseMonitorHotkey` defaults to `Ctrl+Alt+P` and pauses/resumes sensor collection. Paused output and overlays retain the last snapshot; the state is runtime-only and does not persist across restarts. Empty disables it.
- Each hotkey is registered on the WPF main window handle. Invalid or already-claimed combinations fail and are logged to `logs/errors.log`.
- The Windows app uses separate Win32 hotkey IDs for overlay (`0x5048`) and pause (`0x5049`). Registration is updated when settings are applied and removed when the main window closes.

These hotkeys are Windows-specific; the Avalonia Linux frontend does not register them.
