# Updates (GitHub Releases)

> **Language:** English · [Português (Brasil)](../../funções/atualizações.md)

Phantom Monitor checks for newer versions published on GitHub Releases and notifies the UI — and, on Windows and Linux (portable/AppImage), the "Atualizar e reiniciar" (Update and restart) button goes further: it downloads, swaps the files and relaunches on its own. The check is shared by both frontends (implemented in `PhantomMonitor.Services.Update`, in Core); applying the update is platform-specific (appliers).

## How it works

- The source is the public GitHub API (no authentication). The default repository is `BadTonho/PhantomMonitor`; `update.repository` in the config can point elsewhere (`owner/repo` or a GitHub URL).
- The app compares the release **tag** against the version compiled into the executable (csproj `<Version>` at publish time; e.g. tag `v1.2.3` ↔ version `1.2.3`). Older or equal versions are never "installed" (no downgrades).
- A 404 or empty list is treated as "no releases published yet", not as an error.

## Cadence

1. **At startup** (when the automatic check toggle is on);
2. **Every 24 h** while the main window stays open;
3. **"Buscar agora"** (Check now) button in the updates card, anytime.

Network/rate-limit failures are logged to `logs/errors.log` and surfaced as a short message in the card — no modal, no interruption of monitoring.

## Interface (Windows, WPF)

- **"Atualizações" card** (bottom of the page): installed version, last check, "Buscar agora" (Check now), a button that opens the releases page in the browser, and the automatic-check/beta-channel toggles.
- **"Nova versão disponível" banner** in the main content when a newer version exists, with "Ver no GitHub" and — when the swap is possible — the **"Atualizar e reiniciar"** button (shows "Baixando: N %" while downloading and "Reiniciando..." during the swap).
- **"Nova versão" badge** in the sidebar, next to the monitor state; clicking it opens the releases page.

## Interface (Linux, Avalonia)

- **"ATUALIZAÇÕES (GITHUB RELEASES)" card** on the Settings page: automatic-check and beta-channel toggles, a "Repository" field (empty uses the official one), installed version, last check and the "Buscar agora" (Check now), "Abrir página de releases" and — when the swap is possible — "Atualizar e reiniciar" buttons. The preferences go through the page's own "Salvar e aplicar" (Save and apply) button (validation: `owner/repo` or a GitHub URL).
- **"PHANTOM MONITOR · ATUALIZAÇÃO" toast** in the right corner of the dashboard when a newer version exists or a download/swap is in progress, with the same buttons.
- **Update badge** in the sidebar STATUS box (discrete green chip): shows "Nova versão: <tag>", "Baixando: N %" or "Pronto para reiniciar" depending on the state; it is clickable and opens the Settings page (visual unification F1.2, WPF parity).
- On a development build (`0.0.0-dev`), the card states that checking is disabled.

## Applying the update automatically (Windows)

The "Atualizar e reiniciar" button runs the full cycle:

1. Downloads the convention asset `PhantomMonitor-<version>-win-x64.zip` (the portable zip — the same payload used by the per-user installer; in both cases the updater swaps the folder contents).
2. Extracts to `%LOCALAPPDATA%\PhantomMonitor\UpdateStaging\<version>\` (older stagings are cleaned before each preparation).
3. Launches the one-shot updater (`apply_update.cmd` generated into staging): waits for the app process to exit (up to 30 s), swaps files with **backups** (`*.bak`), relaunches the executable and deletes the backups. On a failed copy, the `.bak` is restored immediately — the app never relaunches with half-swapped files.
4. The app closes and reopens on the new version. The user config (`%APPDATA%\PhantomMonitor\config.json`) and logs live outside the swapped folder and are untouched.

Honest limits:

- Before swapping, the app probes its own install folder; if it is protected (a portable extracted into `Program Files`, for example), the swap is not offered and only the notify-with-link mode remains.
- If the release has no asset in the expected format, the button does not appear and the card explains why ("this release does not have the expected file; download manually").
- The executable closes during the swap: the overlay and monitoring stop and resume when the app reopens.

## Applying the update automatically (Linux)

On Linux the flow is detected at startup in two formats:

- **Portable (tarball)** — asset `PhantomMonitor-<version>-linux-x64.tar.gz`: downloads, extracts with the system `tar` into `$XDG_DATA_HOME/PhantomMonitor/UpdateStaging/<version>/` and the one-shot `apply_update.sh` waits for the process to exit (up to 60 s, `kill -0`), swaps files with backups (`*.bak`) — restored on a failed copy — and relaunches the executable with `nohup` when the swap completes.
- **AppImage** (`$APPIMAGE` set) — asset `PhantomMonitor-<version>-linux-x64.AppImage`: the script just replaces the AppImage file, runs `chmod +x` and relaunches it.

Limits and fallbacks:

- The install is tested with a write probe (AppImage: the folder containing the file; portable: the install folder). When swapping is not possible — a `.deb` or other system-package install in a protected directory — the button does not appear and the app stays in notify-with-link mode; the update is done through the system package manager.
- The user config (`~/.config/PhantomMonitor/config.json`) and logs live outside the swapped folder and are untouched.
- The Vulkan layer is not part of the app file swap: after updating, reinstall it with `install_layer.sh` from the new version (see [linux-support.md](linux-support.md)).

## Asset convention and publishing (maintainers)

Releases are generated locally (no publishing CI); the updater looks for these exact names:

| Asset | Used by |
|---|---|
| `PhantomMonitor-<version>-win-x64.zip` | Automatic (Windows applier) + portable |
| `PhantomMonitor-<version>-linux-x64.tar.gz` | Automatic (portable Linux applier) + portable |
| `PhantomMonitor-<version>-linux-x64.AppImage` | Automatic (AppImage applier) |
| `PhantomMonitorSetup-<version>.exe` | Per-user installer (Inno Setup; files match the swapped folder) |
| `phantom-monitor_<version>_amd64.deb` | .deb install (stays in notify mode) |
| `PhantomVulkanLayer-<version>-linux.so` | Vulkan layer (manual; update by re-running that version's `install_layer.sh`) |

Publishing flow:

1. `scripts/release.ps1 -Version <x.y.z>`: publishes both frontends self-contained single-file with `-p:Version=<version>` and generates the zip and tarball under `releases/<version>/`. Options: `-PackWindowsSetup` (installer when Inno Setup 6 is present), `-LayerSoPath <so>` (attaches the compiled Vulkan layer), `-SkipWindows/-SkipLinux` and `-OutDirectory`.
2. On Linux, the experimental packagers (NOT TESTED) live in `packaging/linux/`: `appimage.sh` (appimagetool) and `deb.sh` (dpkg-deb). The Windows installer is `packaging/windows/phantom-monitor.iss`.
3. Review the packages, create the `v<x.y.z>` tag (`git tag v1.2.3 && git push --tags`) and publish: the script prints a `gh release create v1.2.3 ...` command with the assets — or upload everything through the GitHub page.
4. A draft is not seen as an available update; the tag must match the version itself (e.g. `v1.2.3` ↔ `1.2.3`, "v" tolerated by the SemVer parser).

## Channels

- **Stable** (default): uses `GET /releases/latest`, which already skips drafts and prereleases.
- **Beta channel** (toggle): queries `GET /releases` and considers the newest non-draft release, **including prereleases** marked on GitHub; the banner labels it "(pré-release, canal beta)".

Switching the channel does not re-evaluate on its own: use the manual button to re-check on the new channel.

## Development builds

Local builds (`dotnet build`) carry the `0.0.0-dev` marker; they **never** check or update on purpose (running from the repository folder is not updatable). The real version is injected into the release publish (`-p:Version=<tag>`).

## Configuration

```json
"update": {
  "autoCheck": true,
  "checkBeta": false,
  "repository": ""
}
```

- `autoCheck` — check at startup + a 24 h timer (the manual button always works). The toggle persists in the config.
- `checkBeta` — beta channel (above).
- `repository` — empty uses the official repository; accepts `owner/repo` or a GitHub URL (including a `.git` suffix).

See [configuration.md](configuration.md) for the full config schema.
