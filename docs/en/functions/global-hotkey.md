# Global hotkeys

> **Language:** English · [Português (Brasil)](../../funções/hotkey.md)

In **Settings → Overlay mode**, select **Definir atalho**, press a combination, and choose **Salvar e aplicar**. The shortcut toggles the floating or in-game overlay even while another application has focus. **Limpar atalho** disables it; `Esc` cancels capture. The default is disabled.

Supported keys are `A–Z` and `0–9` with at least one modifier (`Ctrl`, `Alt`, `Shift`, or `Win`), or `F1–F24` with or without modifiers. Conflicts with the pause shortcut or another application are shown in the interface. Changes take effect after saving; if saving fails, the app attempts to restore the previous registration and reports restoration failures.

## Platforms

- **Windows:** `RegisterHotKey` sends events to a message-only window on a dedicated thread. Changing the overlay shortcut preserves the pause shortcut; no keyboard hook is installed.
- **Linux X11:** a dedicated XCB connection registers the combination with Caps Lock, Num Lock, and Scroll Lock variations. It requires `libxcb` and retains the previous registration on conflict.
- **Linux Wayland:** uses `org.freedesktop.portal.GlobalShortcuts` on the session D-Bus. The desktop may ask for confirmation or select another combination; the interface displays the effective description returned by the portal. Availability depends on the portal and compositor. XWayland is not treated as a global Wayland fallback.

The Linux implementations still need validation in real X11 and Wayland sessions. The pause hotkey remains Windows-only. The existing interface and tray controls remain available when global shortcuts are unavailable.

## Configuration

`toggleOverlayHotkey` stores the preferred combination in `config.json`, for example `Ctrl+Shift+O`, and remains empty by default. It can also be selected in Settings. `pauseMonitorHotkey` remains configurable in the file and defaults to `Ctrl+Alt+P` on Windows; an empty value disables it. Existing configurations are preserved.
