# Phase 1 — WPF → Avalonia equivalence map

> **Language:** English · [Português (Brasil)](../unificacao-fase1-mapa.md)

> Audit completed in October 2026 based on reading both frontends end to end.
> Sources: `src/PhantomMonitor` (WPF: `MainWindow.xaml`/`.cs`, `Overlay/*`,
> `DebugWindow`, `InventoryWindow`, `Services/`) and `src/PhantomMonitor.App`
> (Avalonia: views, `ViewModels/*`, `Services/*`), plus `PhantomMonitor.Core`.
> Decision rule: **the UI that already exists on Avalonia is the target format**
> (views in the sidebar); what WPF has extra gets ported; what Avalonia has
> extra stays. Everything renders the two Phase 0 built-in themes
> (`Neptune`/`Slate`).

---

## 1. Format decisions (window by window)

| WPF | Target format (Avalonia) | Status today |
|---|---|---|
| `MainWindow` (shell) | `Views/MainWindow.axaml` (7 sidebar views) | Structural parity; see §2 |
| `Overlay/OverlayWindow` | Its own window (already exists) | Port P3 profiles, graph rows, click-through (Windows) |
| `Overlay/OverlayEditorWindow` | "OVERLAY FLUTUANTE" section in Settings (format already exists) | Port profiles; quick positioning/preview are nice-to-have |
| `Overlay/AlertPopupWindow` | In-app toast (already exists) + DBus notification on Linux (already exists) | Per-OS coexistence (toast on Windows, DBus on Linux) |
| `DebugWindow` | Diagnostics view (already exists, near parity) | Absorb; extra: copyable text dump |
| `InventoryWindow` | Inventory view (partial) | Port session stats, hardware tree, monitors (EDID), reset |

Visual junk found in WPF: the "SENSORES" `RadioButton`s in the sidebar
(CPU/GPU/RAM/Disk/Net) **have no handlers** — do not port them.

## 2. Dashboard (Overview view)

Target shape = Avalonia layout (cards with charts), absorbing what's missing:

| Feature | WPF | Avalonia | Phase 1 action |
|---|---|---|---|
| CPU/GPU cards with line chart | ❌ (bar+pill) | ✅ | keep the Avalonia format |
| Temperature with dynamic pills (color by range) | ✅ | partial (text) | port range colors (AccentWarning etc.) |
| Detail lines (CPU: clock/power/voltage; GPU: VRAM/clock/hotspot) | ✅ (`SensorFormatter`) | ❌ | port (formatter is already Core) |
| FPS + stats (min/avg/1%/0.1%) | ✅ | ✅ | same |
| Frametime chart on the dashboard | ❌ | ✅ | keep Avalonia |
| Disk card (R/W) | ✅ | ✅ | same |
| Network card (↓/↑) | ✅ | ✅ | same |
| RAM card (usage/total/available) | ✅ | ✅ | same |
| Motherboard & fans card | ✅ | ❌ | port |
| **GPU process list** | ✅ (`data.GpuProcesses`) | ❌ | port |
| "New version" badge in the sidebar | ✅ | (toast only) | port a discreet badge |
| Update banner | ✅ | (settings card + toast) | keep the Avalonia format |
| Admin elevation banner + restart as admin | ✅ | ❌ (Linux, by nature) | use on the single app (Windows) |
| CSV/JSON/JSONL toggles in the sidebar | ✅ | in Settings | keep the Avalonia format |
| `Minigraph` on the dashboard | ❌ (overlay only) | HistoryChart | Minigraph stays overlay-only until charts unify |

New tokens needed in Phase 1: the red family for hot usage/admin banner
(WPF uses `#EF4444`/`#FCA5A5`/`#F87171`/`#DC2626`) and the update green
(`#059669`/`#A7F3D0`) — the 30-token catalog does not cover them yet.

## 3. Windows and services — what gets ported, in detail

### Overlay (`Overlay/OverlayWindow`, 1227 lines of code-behind in WPF)

| Feature | WPF | Avalonia | Action |
|---|---|---|---|
| Drag by header | ✅ | ✅ | same |
| Hide button | (own behavior) | ✅ | keep |
| Compact mode | ✅ | ✅ | same |
| Per-instance series whitelist | ✅ | ✅ | same |
| **Per-app profiles (P3)** + tray pin | ✅ (`RefreshProfileFor`/`ApplyProfile`) | ❌ | port (data already in `config.profiles`) |
| **Graph rows per series** (Minigraph with guides/min/max) | ✅ | ❌ (frametime only) | port with the unified HistoryChart |
| True click-through (`WS_EX_TRANSPARENT`) | ✅ | ❌ (receives clicks) | on Windows via P/Invoke; keep Linux as is |
| Topmost timer / position by focused app | ✅ | partial (fixed Topmost) | port |
| Per-profile accent color | ✅ | ❌ | port |

### System services

| Service | WPF (where) | Avalonia | Action |
|---|---|---|---|
| Tray + menu (overlay/startup/reset/pause/pin profile) | `TrayIconService` (369 ln) | ❌ | port (Avalonia TrayIcons; per-OS APIs) |
| Global hotkeys (overlay + pause, P6.7) | `HotkeyService` (96 ln) + `HotkeyParser` (Core) | ❌ | port (X11/XWayland; plain Wayland: document the limitation) |
| Start with Windows (`Run` registry) | `StartupManager` (56 ln) | ❌ | port (registry on Windows; desktop entry on Linux) |
| Single instance | `App.xaml.cs` (mutex) | verify | verify/port |
| Admin elevation | inline (`IsRunningAsAdmin`/`runas`) | ❌ | reuse on the single app |
| Alerts: evaluator | `AlertRuleEvaluator` **Core** ✅ | ✅ | same |
| Alerts: actions | popup + `MessageBeep` + script + shutdown (with `allowShutdown` guard) | script + DBus (sound/shutdown not executed) | extract orchestration into Core with per-OS actions |
| Alert rules UI (editor) | ❌ (config.json) | ✅ | keep Avalonia |
| Updates | ✅ (Core + Windows applier) | ✅ (Core + Linux applier) | no gap |
| Custom sensors / series tuning / HTTP exporter UIs | ❌ | ✅ | keep Avalonia |

### Sensor reading — the critical finding (F1.1 ✅ done)

- **F1.1 shipped:** `HardwareMonitorService` (1124 ln), `FpsMonitor`
  (495 ln — PresentMon) and `Win32Interop` moved into
  `Core/Services/Windows/`; `InventoryBuilder` (92 ln, multi-platform) into
  `Core/Services/`. The cross-platform `SensorServiceFactory` now lives in
  Core and replaced `LinuxSensorServiceFactory` — both frontends boot the
  right provider per OS (`SensorServiceFactory.Create(updateIntervalMs, config)`),
  with the `OperatingSystem.IsWindows()` guard +
  `[SupportedOSPlatform("windows")]` and the Windows packages
  (LibreHardwareMonitor, System.Management, PerformanceCounter, EventLog) in
  Core. `PresentMon.exe` now ships with the App binary; the smoke test
  validated Avalonia and WPF reading Windows sensors through the same Core
  code.
- `PhantomSensorLib` is not consumed by .NET (zero `DllImport` in the repo) —
  it is used by the Vulkan layer (C++). It does not affect the port.

## 4. Suggested order (Phase 1 sub-phases)

1. **F1.1 — Windows services → Core** (+ cross-platform factory; Avalonia
   monitoring on Windows). Pure code, no UI — kills the critical risk early.
   **✅ Done (see "Sensor reading" above).**
2. **F1.2 — Complete dashboard on Avalonia** (detail lines, GPU processes,
   MB&fans, badge/banners) with both themes rendering.
3. **F1.3 — Complete overlay** (P3 profiles + pin, graph rows, Windows
   click-through) and settings with the missing bits (quick positioning/preview).
4. **F1.4 — Complete inventory** (session stats, hardware tree, monitors/EDID,
   reset with CSV/journal marker).
5. **F1.5 — System** (tray, hotkeys, startup, single instance, admin) and
   alerts (per-OS action orchestration).
6. **F1.6 — WPF visual ported entirely as the "Slate" theme** + Windows
   release build of the Avalonia binary (Phase 1 acceptance criteria on the
   roadmap).

## 5. Port size (reference)

| WPF file | Lines | Destination |
|---|---|---|
| `Overlay/OverlayWindow.xaml.cs` | 1227 | Avalonia overlay (profiles/chart/click-through) |
| `Services/HardwareMonitorService.cs` | 1124 | Core/Services/Windows |
| `Overlay/OverlayEditorWindow.xaml.cs` | 596 | config apply (Settings view) |
| `Services/FpsMonitor.cs` (PresentMon) | 495 | Core/Services/Windows |
| `Services/TrayIconService.cs` | 369 | Avalonia tray (multi-OS) |
| `Overlay/OverlayEditorWindow.xaml` | 354 | visual reference for the Settings view |
| `MainWindow.xaml.cs` | 739 | Avalonia ViewModel (logic already partially there) |
| `DebugWindow`/`InventoryWindow`/`AlertPopup` | ~490 | Diagnostics/Inventory/toast views |
| `Services/HotkeyService.cs` | 96 | Avalonia hotkeys |
