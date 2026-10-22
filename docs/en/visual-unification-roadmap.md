# Roadmap — Visual unification (single Avalonia frontend + theming)

> **Language:** English · [Português (Brasil)](../roadmap-unificacao-visual.md)

> Plan approved in October 2026 — **Phase 0 completed** (`0a6ee28` = plan,
> `0a00bf6` = Neptune/Slate tokens); Phases 1–3 pending. Origin: comparison between the WPF (Windows) and Avalonia
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
| `Slate` | Today's WPF look | `src/PhantomMonitor/Resources/Styles.xaml` (dark neutral, blue accent `#2563EB`) |
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
- [ ] Shared Avalonia controls: selected-state nav, cards, inputs, banners
      (including the update banner), accent gradients.
- [ ] Windows-specific features to verify in the port:
      admin elevation, PresentMon (external process — stays as is, via Core),
      global hotkey, tray, Windows startup, DebugWindow (keep or drop).
- [ ] Native sensor library (`PhantomSensorLib`) via P/Invoke delivering the
      same sensors on the Avalonia Windows binary.
- [ ] Unify charts: `Controls/Minigraph.cs` (WPF) and `Controls/HistoryChart.cs`
      (Avalonia) become **one** control with token-driven colors.
- [ ] Port the WPF look as `Themes/Tokens.Slate.axaml` (2nd factory theme).
- [ ] Windows release build producing the Avalonia binary — full manual test
      (admin elevation, fps, overlay, tray, updates) + docs.

**Acceptance criteria:** 1:1 functional parity with today's WPF on Windows;
green build; both built-in themes render the same views.

## Phase 2 — Retire WPF

- [ ] `scripts/release.ps1` publishes only the Avalonia app (win-x64 +
      linux-x64).
- [ ] `packaging/windows/phantom-monitor.iss` points to the final executable.
- [ ] **Updater continuity decision:** keep asset names (`PhantomMonitor-<v>-win-x64.zip`
      / `-linux-x64.tar.gz` / `.AppImage`) — no updater changes. Define the
      final AssemblyName (recommended: unify on `PhantomMonitor.exe` to keep
      the setup shortcut, Start Menu and post-update relaunch) + how the
      portable swap handles the leftover old executable (`.bak`), or document
      that the transition release accepts a full reinstall on Windows.
- [ ] Remove `src/PhantomMonitor` (separate `chore:` commit, retiring
      `Styles.xaml`, the windows and `Controls/Minigraph.cs` together).
- [ ] Docs synchronized: READMEs (en/pt), `main-interface` /
      `interface-principal`, `updates` / `atualizações`, packaging; the root
      README line saying "WPF remains the primary app" becomes single-app.

**Acceptance criteria:** Windows runs the single app; release/update pipeline
untouched; WPF out of the repo; no users with broken shortcuts.

## Phase 3 — User-facing theme system

- [ ] `config.json` + `Config.Normalize`: `theme: "neptune" | "slate"` field
      (default TBD — decided on the commit).
- [ ] Picker under **Settings** (same pattern as the other settings), applying
      at runtime via dictionary swap (dynamic `Application.Styles` or
      `ResourceDictionary` merge/unmerge) — UI reacts instantly thanks to the
      Phase 0 `DynamicResource`.
- [ ] Persistence and re-load on boot.
- [ ] (Future, out of scope now) extra themes: Light mode, custom via external
      file, import/export.

**Acceptance criteria:** theme swap without restart, no view rebuilds,
persisted across sessions; configuration docs updated.

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
