# Roadmap — Visual unification (single Avalonia frontend + theming)

> **Language:** English · [Português (Brasil)](../roadmap-unificacao-visual.md)

> Plan approved in October 2026 — **Phases 0–3 completed** (Phase 0: `0a6ee28` =
> plan, `0a00bf6` = Neptune/Slate tokens; Phase 3 = `c824f77`). Origin: comparison
> between the WPF (Windows) and Avalonia
> (Linux) frontends. The two looks have drifted since day one and every UI
> change costs double. Maintainer's decision: adopt the **"single app" path**
> keeping both current looks as built-in themes, which also front-loads the
> foundation of the future theme system.

---

## Goal

1. **Unify the frontend:** retire WPF and keep Avalonia as the single
   cross-platform app (`net10.0`, win-x64 + linux-x64). `PhantomMonitor.Core`
   and services keep being shared, as today.
2. **Preserve both current looks:** each appearance becomes a token-based
   **built-in theme** — nothing about today's visuals is lost.
3. **Front-load the future theme system:** swappable tokens at runtime (no
   view rebuilds), persisted in `config.json`.

## Why phases in this order

**Tokenizing Avalonia comes before the migration** (Phase 0 before Phase 1):
the migration is born "themable" (the WPF look is ported as the second factory
theme) and the UI is never moved twice.

## Architectural decisions (the foundations)

1. **Tokens, never inline hex:** colors, typography, corner radii and paddings
   live in `Themes/Tokens.<Theme>.axaml` (~20–25 keys per theme). No view sees
   absolute values.
2. **Shared controls:** a single set of styles for nav/cards/buttons/inputs/
   banners. A theme swaps **tokens only** — never duplicate UI per theme.
3. **`{DynamicResource}`** (not `StaticResource`) for every themable
   color/value: prerequisite for runtime theme switching on a live UI.
4. **Native `ThemeVariant`** (Avalonia, Light/Dark) per theme — already in use
   today (`RequestedThemeVariant="Dark"`).
5. **Minimal initial scope:** the two built-in themes (below) + manual swap by
   editing `App.axaml`. Out of initial scope: Light mode, custom themes via
   external files, import/export, live preview.

### Working theme names

| Working theme | Origin | Reference |
|---|---|---|
| `Slate` | The WPF look (retired in Phase 2) | `src/PhantomMonitor/Resources/Styles.xaml` (removed with the WPF; verbatim tokens in `src/PhantomMonitor.App/Themes/Tokens.Slate.axaml`) — dark neutral, blue accent `#2563EB` |
| `Neptune` | Today's Avalonia look | `src/PhantomMonitor.App/Views/MainWindow.axaml` (blue-gray) |

> Names finalized in Phase 0 (`0a00bf6`): `Slate` = the WPF look; `Neptune` = the Avalonia look.

## What does NOT survive 1:1 (honest expectations)

- **Exact font:** Segoe UI does not exist on Linux; the theme defines a
  fallback stack. Font rendering belongs to the host OS — "looks equal", not
  pixel-perfect.
- **Window chrome:** Windows Mica/acrylic/native titlebar does not exist on
  Linux; Avalonia draws a custom titlebar that approximates it, but that is
  migration work, not theming.
- Bottom line: **palette/typography/style are preserved; the platform chooses
  the rendering.**

---

## Phase 0 — Tokenize the current Avalonia app — ✅ DONE (`0a6ee28`, `0a00bf6`)

- [x] Create `src/PhantomMonitor.App/Themes/Tokens.Neptune.axaml` with the
      current Avalonia palette (every inline hex today) + metrics (radii,
      paddings, type scale, font stack).
- [x] Replace hardcoded hex in `Views/MainWindow.axaml` and
      `Views/OverlayWindow.axaml` with `{DynamicResource …}` (keys mirrored to
      the WPF dictionary).
- [x] `App.axaml` loads the active dictionary — merge structure ready for
      Phase 3.
- [x] Smoke test: manually swap between the two dictionaries by editing
      `App.axaml` — the whole UI changes look without breaking anything.
- [x] Docs: token note in `docs/en/functions/main-interface.md`
      (+ Portuguese source `docs/funções/interface-principal.md`).

**Outcome (`0a00bf6`):** 30 tokens per dictionary (`Neptune` = the Avalonia
look verbatim; `Slate` = the WPF palette from `Styles.xaml`, with same-family
estimates for tokens with no WPF equivalent). Metrics (radii/paddings/
typography) land in Phase 1, together with the shared controls. Technical
detail: `HistoryChart.Stroke` went from `Color` to `IBrush?` (a color-typed
property cannot consume a theme brush). Validation: Release build 0/0, full
static coverage (30 tokens used, no orphans) and runtime smoke on both themes
(app alive for 10 s with a clean kill).

## Phase 1 — Mapping and porting the WPF screens (the bulk)

First task, before anything else: **map equivalences** between windows/flows.

- [x] WPF → Avalonia equivalence map (window vs sidebar view):
      `MainWindow`, `OverlayWindow`, `OverlayEditorWindow`, `AlertPopupWindow`,
      `InventoryWindow`, `DebugWindow` — decide the final format window by
      window. **✅ Done — see [unification-phase1-map.md](unification-phase1-map.md)
      (per-window decisions, gaps, Windows services → Core and sub-phases F1.1–F1.6).**
- [x] **F1.1 — Windows sensor services → Core**: `HardwareMonitorService`,
      `FpsMonitor` (PresentMon) and `Win32Interop` into `Core/Services/Windows/`;
      `InventoryBuilder` (multi-platform) directly into `Core/Services/`; single
      cross-platform `SensorServiceFactory` in Core (Avalonia stops booting
      `LinuxSensorServiceFactory` directly); Windows packages in Core
      (LibreHardwareMonitor, System.Management, PerformanceCounter, EventLog)
      under `[SupportedOSPlatform("windows")]`; PresentMon now ships with the
      App binary. Smoke: Avalonia and WPF collecting Windows sensors through
      the same Core code.
- [x] Shared Avalonia controls: selected-state nav (3×14 indicator + selected
      background via `Nav*` tokens, F1.6), cards, inputs and tokenized banners
      (the alert toast gained `AlertToast*` in F1.6, including the update
      banner already token-driven). The WPF "gradient/accent" was dead code
      (defined in Styles.xaml, never consumed) and was not ported; the CTAs
      (`Button.action`) follow the platform accent (Fluent, dynamic on
      Windows) in both themes — a deliberate Neptune-preservation decision.
- [x] **F1.2 — Complete dashboard on Avalonia**: temperature pills with color
      by range (green/warm/hot/unknown) and painted usage metrics (<75
      primary, <90 warm, ≥90 hot — WPF parity); mono detail lines for CPU/GPU
      via the Core `SensorFormatter`; **Motherboard & fans** card (colored
      temp + `BuildFanSummary`); **GPU active processes** card (WMI GPUEngine;
      empty state on Linux); update badge in the sidebar STATUS box (click →
      Settings); admin elevation banner with "Run as admin" (UAC) on Windows.
      New tokens in both themes: hot family
      (`AccentHotBrush`/`AccentHotSoftBrush`/`AccentHotBodyBrush`) and
      translucent tints (`BgTintGreen/Warm/HotBrush` +
      `BorderTintGreen/Warm/HotBrush`, reused by badge and banner).
- [x] **F1.3 — Complete overlay on Avalonia**: per-app profiles (P3 —
      `AppMatch` matcher of the focused process with pin peeking via
      `_pinnedProfileName`; override of offsets/width/font/opacity/whitelist/
      accent/visibility on top of the global config; "◉ profile: X" tag),
      graph rows per series with fixed axis (HistoryChart extended with
      `MinY`/`MaxY`/`MaxFloor`/`GuideAt`/`GuideBrush`) and P6.6 templates —
      parser extracted to Core (`OverlayPlaceholderParser`); true
      click-through (`WS_EX_TRANSPARENT`), topmost (500 ms) and
      position-by-focused-app (30 ms) on Windows; OSD accent as config data
      (`OverlayBrushHelper`) with a new `OverlayGuideBrush` token. On Windows
      the panel is click-through like the WPF (no drag/no clickable ×); on
      Linux it stays draggable. **Pending:** tray pin (F1.5) and quick
      positioning/preview (re-evaluate alongside the system subphase).
- [x] **F1.4 — Complete inventory on Avalonia**: session statistics
      (minimum/maximum/average) on the group rows, fed by `data.SeriesStats`
      with the same value formatter (per-stat magnitude conversions; NaN =
      "--"); **Detected hardware** card with the `[HWR]` tree
      (`InventoryBuilder.BuildHardware`, rebuilt only when the node signature
      changes — anti-collapse like the WPF); **Monitors (EDID)** card with
      WMI instance/manufacturer/model/code/serial/diagonal/manufacturing and
      the PRIMARY flag (collection changes only when the set changes);
      **Reset statistics** button with `ResetSeriesStats` +
      `CsvLogger.WriteMarker` + `JsonJournalLogger.WriteNote` and a transient
      confirmation; summary gains "N nodes · M monitor(s)" when present.
- [x] **F1.5 — System**: tray on the unified app (`TrayIconService` — the
      same WPF items/checks, left-click activation, runtime-drawn icon;
      StatusNotifier on Linux) with profile pinning from the tray
      (`SetPinnedProfile`, closes the F1.3 pending item); global hotkeys on
      Windows (`GlobalHotkeyService` in Core, message-only window on a
      dedicated thread, IDs `0x5048`/`0x5049` — Linux deferred: X11/XWayland in
      the future, pure Wayland disallows them by design); startup with the
      system per OS (`StartupManagerFactory` — Run registry on Windows, XDG
      autostart on Linux) with the "Start with the system" checkbox; single
      instance (`SingleInstanceGuard` — `Local\PhantomMonitor.SingleInstance`
      mutex on Windows, file lock on Linux); per-OS alert actions extracted to
      Core (`IAlertActions`/`AlertActionFactory` — Windows with
      beep/script/shutdown + `allowShutdown` guard at WPF parity, Linux with
      script + desktop notification); per-OS update applier (Windows zip /
      Linux tarball). Admin elevation had no gap since F1.2. (`9f49ba0`)
- [ ] Windows-specific features to verify in the port:
      admin elevation ✅ (F1.2/F1.5), PresentMon (external process — stays as is, via Core),
      global hotkey ✅ (F1.5, Windows), tray ✅ (F1.5), startup ✅ (F1.5),
      DebugWindow (keep or drop).
- [ ] Native sensor library (`PhantomSensorLib`) via P/Invoke delivering the
      same sensors on the Avalonia Windows binary.
- [x] Unify charts — **resolved with the WPF retirement (Phase 2)**: the WPF
      never consumed the Minigraph in the dashboard; the control was removed
      with the frontend (`6c4d2c0`) and the `HistoryChart` (token-driven
      colors) is the app's only chart.
- [x] **F1.6 — WPF visual ported as the complete "Slate" theme + Windows
      build/release** (`ae01a89`): `Tokens.Slate.axaml` rewritten with the WPF
      verbatim values (Styles.xaml palette, banners/pills from `MainWindow.xaml`,
      OSD panel `#CC070A0F`/`#25FFFFFF`, `AlertPopupWindow` toast — per-line
      provenance in the dictionary itself), `Nav*` tokens + selected nav state
      with the 3×14 indicator (port of the WPF RadioButton), `AlertToast*`
      tokens for the alert toast, `uiTheme` (neptune|slate) in `config.json`
      applied at boot — CLI `--ui-theme=<name>` overrides it for smoke; runtime
      switching by UI arrives in Phase 3 — and a validated win-x64
      self-contained single-file publish (`releases/win-x64/` with
      PresentMon.exe + config.example.json; green build and live smoke in both
      themes without `errors.log`). PhantomSensorLib P/Invoke remains its own
      item below; coverage revalidated (46 homonymous keys in both
      dictionaries, zero orphans).

**Acceptance criteria:** 1:1 functional parity with today's WPF on Windows;
green build; both built-in themes render the same views.

## Phase 2 — Retire WPF

- [x] **Phase 2 — WPF retired from the pipeline and the repo** (`a6f3e7f` feat +
      `6c4d2c0` chore):
      `scripts/release.ps1` publishes only the unified app (win-x64 +
      linux-x64) — the Windows payload is capped at `PhantomMonitor.exe` +
      `PresentMon.exe` + `config.example.json` (the single-file bundler
      embeds PresentMon as native and scatters `.pdb`s; the script guarantees
      the loose copy and strips the pdbs), tarball holds the `PhantomMonitor`
      binary + `config.example.json`; missing ISCC becomes a warning instead
      of an abort (pipeline validated end to end with `-Version 0.2.0-dev`:
      44.1 MB zip, 40.7 MB tarball).
- [x] `packaging/windows/phantom-monitor.iss` points to the final executable
      **with no edits**: with the unified `AssemblyName`, the payload exe is
      born exactly as `{app}\PhantomMonitor.exe`, the name the stage Source
      expects.
- [x] **Updater continuity decision:** asset names kept as the current
      convention (`PhantomMonitor-<v>-win-x64.zip` / `-linux-x64.tar.gz` /
      `.AppImage`) — no updater changes. Final `AssemblyName` =
      **`PhantomMonitor`** (recommendation adopted: the setup shortcut, Start
      Menu, Run key and post-update relaunch stay valid, and the PresentMon
      `--exclude PhantomMonitor.exe` matches the real app name on Windows
      again). The portable swap leaves no orphan: on Windows the name is
      identical (old exe goes to `.bak`, the new one takes its place); on
      Linux the transition tarball carries a `PhantomMonitor.App` hardlink to
      the new binary at zero tar cost, so legacy installs relaunching by the
      old name boot the new app (shim can be retired in upcoming releases).
      `avares://PhantomMonitor/...` URIs updated with the rename.
- [x] Remove `src/PhantomMonitor` (separate `chore:` commit: `6c4d2c0`,
      retiring `Styles.xaml`, the windows and `Controls/Minigraph.cs`
      together — 24 files; slnx build 0/0). The Phase 1 "unify the charts"
      item resolves here: without the WPF, `HistoryChart` is the only chart
      control.
- [x] Docs synchronized (following `docs:` commit): READMEs en/pt,
      `main-interface` / `interface-principal`, `updates` / `atualizações`,
      `configuration` / `configuracao` (visual editor retired), parity
      matrices and `AGENTS.md`; the "WPF remains the primary app" line now
      describes the single app.

**Acceptance criteria — met:** Windows runs the single app; release/update
pipeline intact (validated with a local 0.2.0-dev release, zip/tarball
checked by listing); WPF out of the repo; no users with broken shortcuts
(the executable name is preserved on Windows and a hardlink shim covers
Linux). Both themes smoke-tested without `errors.log`.

## Phase 3 — User-facing theme system

- [x] `config.json` + `Config.Normalize`: `theme: "neptune" | "slate"` field
      (`c824f77`). **Default = `neptune`** (commit decision: keeps the Avalonia
      look as the baseline and avoids visual churn for whoever never picks a
      theme; `slate` stays opt-in, also via the smoke CLI). F1.6-era configs
      holding `uiTheme` migrate on load (the file only writes `theme` now).
- [x] **Theme visual** picker under the **INTERFACE** card of the Settings page
      (same pattern as the other settings), applied on **Save and apply** via
      `App.ApplyTheme`: a runtime swap of `MergedDictionaries` — views react
      instantly through the Phase 0 `DynamicResource` (including
      `HistoryChart`), and the ViewModel-resolved brushes (`ResolveBrush`) are
      re-resolved on the `ThemeChanged` event (brush cache invalidated +
      `UpdateDashboardExtras`/`ApplyOverlayProfile`/`UpdateOverlayRows`), with
      no view rebuilds and no waiting for the next tick while collection is
      stopped. A failed config save rolls the pending swap back.
- [x] Persistence in `config.json` (via `AppConfig.Save()`) and re-load at boot
      (`ApplyConfiguredTheme` — dictionary swapped before creating the views).
      Canonical CLI `--theme=<name>`; legacy `--ui-theme=` is still accepted.
- [ ] (Future, out of scope now) extra themes: Light mode, custom via external
      file, import/export.

**Acceptance criteria — met:** theme swap without restart, no view rebuilds,
persisted across sessions; build 0/0; live boot smoke with `--theme=slate` and
with a legacy `uiTheme` config (migration on load), both with nothing logged in
`errors.log` — the same dictionary-swap path (`App.ApplyTheme`) runs at boot
with an overridden theme, so the mechanics were exercised; the UI-driven swap
(INTERFACE card) takes the same path and manual picker smoke remains pending
confirmation. Configuration docs updated (`configuration` +
INTERFACE notes in `main-interface`).

## Later enhancement — visual overlay editor

- [x] Rebuild the editor in Avalonia with a gridded live canvas on the left,
      customization controls on the right, and draggable blocks.
- [x] Store normalized positions in `overlay.layout`, relative to the monitored
      window on Windows and the primary screen on Linux; existing configs start
      with a vertical layout.
- [x] Render each block in its own window to preserve Windows click-through and
      avoid a full-screen transparent window capturing Linux input.
- [x] Update the Portuguese and English overlay/configuration documentation.

---

## Risks and care

| Risk | How the phase handles it |
|---|---|
| Losing a Windows-specific feature in the port | The equivalence map is the **first** Phase 1 task; 1:1 functional parity is an acceptance criterion |
| Theme swap not updating live views | `DynamicResource` from Phase 0 on (hence the phase order) |
| Portable Windows user with a leftover old executable after update | Decision documented in Phase 2 (final executable name + `.bak` + transition release changelog) |
| Visual drifting apart again | Repo rule: **inline hex in `.axaml` is a review bug** |
| Duplicated work with Round 5 | See section below |

## Relation to Round 5 / feature-parity roadmap

Round 5 polish items that touch XAML (OSD panel, global axis, inventory
polish) are worth executing **once** — on the unified app. Suggested order for
the work queue:

1. **Phase 0** (tokenization — cheap).
2. Remaining Round 5 items **only where they don't double the work** (Linux
   items such as P1/P99 percentiles can land directly on Avalonia).
3. **Phases 1 → 2 → 3** of this plan, absorbing the remaining Round 5 XAML
   work (panel/axis/polish) inside the port.

## Reference inventory (what defines each look today)

> Historical reference: the `src/PhantomMonitor/**` files below were removed
> with the WPF in Phase 2 (`6c4d2c0`). The current source of truth for the
> "Slate" look is `src/PhantomMonitor.App/Themes/Tokens.Slate.axaml`.

| File | Role |
|---|---|
| `src/PhantomMonitor/Resources/Styles.xaml` (254 lines) | Named palette + WPF styles — **source of truth for the "Slate" look** |
| `src/PhantomMonitor/MainWindow.xaml` (477 lines) | WPF shell |
| `src/PhantomMonitor/{Debug,Inventory,AlertPopup,OverlayEditor,Overlay}Window.xaml` | WPF windows to map in Phase 1 |
| `src/PhantomMonitor/Controls/Minigraph.cs` | WPF chart (unify with HistoryChart) |
| `src/PhantomMonitor.App/Views/MainWindow.axaml` (706 lines) | Avalonia shell (consumes `{DynamicResource}`) — the "Neptune" source of truth now lives in `Themes/Tokens.*.axaml` |
| `src/PhantomMonitor.App/Views/OverlayWindow.axaml` | Avalonia overlay (consumes `{DynamicResource}`) |
| `src/PhantomMonitor.App/Themes/Tokens.{Neptune,Slate}.axaml` | Theme dictionaries (30 tokens each) — `0a00bf6` |
| `src/PhantomMonitor.App/App.axaml` (8 lines) | `FluentTheme` + `RequestedThemeVariant="Dark"` |
| `src/PhantomMonitor.App/Controls/HistoryChart.cs` | Avalonia chart (unify with Minigraph) |
| `src/PhantomMonitor.Core/Services/Update/` | Update core — **unchanged** (release detection, banners and apply stay as is) |

## Documentation obligations (per phase)

- Phase 0: token note in `main-interface` / `interface-principal`.
- Phase 1: `main-interface` reflecting the single app; windows mapped.
- Phase 2: READMEs (en/pt), `updates` / `atualizações` (if the setup payload
  changes), packaging + build scripts in configuration docs.
- Phase 3: `configuration` / `configuracao` with the `theme` field.
