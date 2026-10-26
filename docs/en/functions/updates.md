# Updates (GitHub Releases)

> **Language:** English · [Português (Brasil)](../../funções/atualizações.md)

Phantom Monitor checks for newer versions published on GitHub Releases and notifies the UI — and, on Windows and Linux (portable/AppImage), the "Atualizar e reiniciar" (Update and restart) button goes further: it downloads, swaps the files and relaunches on its own. The check is shared by all platforms (implemented in `PhantomMonitor.Services.Update`, in Core); applying the update is platform-specific (appliers).

## How it works

- The source is the public GitHub API (no authentication). The default repository is `BadTonho/PhantomMonitor`; `update.repository` in the config can point elsewhere (`owner/repo` or a GitHub URL).
- The app compares the release **tag** against the version compiled into the executable (csproj `<Version>` at publish time; e.g. tag `v1.2.3` ↔ version `1.2.3`). Older or equal versions are never "installed" (no downgrades).
- A 404 or empty list is treated as "no releases published yet", not as an error.

## Cadence

1. **At startup** (when the automatic check toggle is on);
2. **Every 24 h** while the main window stays open;
3. **"Buscar agora"** (Check now) button in the updates card, anytime.

Network/rate-limit failures are logged to `logs/errors.log` and surfaced as a short message in the card — no modal, no interruption of monitoring.

## Interface (unified app)

The same elements serve Windows and Linux (the WPF details were ported during the visual unification):

- **"PHANTOM MONITOR · ATUALIZAÇÃO" toast** in the right corner of the dashboard when a newer version exists or a download/swap is in progress, with the same buttons (on the WPF it was the "Nova versão disponível" banner in the main content, with "Ver no GitHub" and the **"Atualizar e reiniciar"** button — it shows "Baixando: N %" while downloading and "Reiniciando..." during the swap).
- **Update badge** in the sidebar STATUS box (discrete green chip): shows "Nova versão: <tag>", "Baixando: N %" or "Pronto para reiniciar" depending on the state; it is clickable and opens the Settings page (visual unification F1.2, WPF parity).
- **"ATUALIZAÇÕES (GITHUB RELEASES)" card** on the Settings page: automatic-check and beta-channel toggles, a "Repository" field (empty uses the official one), installed version, last check and the "Buscar agora" (Check now), "Abrir página de releases" and — when the swap is possible — "Atualizar e reiniciar" buttons. The preferences go through the page's own "Salvar e aplicar" (Save and apply) button (validation: `owner/repo` or a GitHub URL).
- On a development build (`0.0.0-dev`), the card states that checking is disabled.

## Transactional application

Downloads require the GitHub API SHA-256 `digest` and reject missing or mismatched
hashes. Trust still rests on HTTPS and the selected repository; a digest is not a
maintainer signature. See the [GitHub asset API](https://docs.github.com/en/rest/releases/assets).

Windows extracts the full directory tree and runs `apply_update.ps1` through
system PowerShell. Close games using the native hooks before updating. The app
checks native DLL locks before shutdown; the script waits up to 60 seconds for
exit, prepares all copies and updates renderer subfolders as well as root files.
Handled failures restore the full changed set and remove newly added files.

Linux extracts tarballs through .NET without shell interpolation or an external
tar command. AppImage preparation/application share a single version-directory
path. The POSIX script prepares the tree before swapping entries and restores
all changed entries on a handled failure. AppImage replacement uses a prepared
file and rename on the target filesystem. Spaces and apostrophes are supported.

Recovery backups `.phantom-update-backup-*` are retained. Windows backups contain
`status`, `journal.json` and `original/`; portable Linux includes installed/original
entry lists. Remove backups manually only after verifying the new installation
with the app closed. Power loss or failure during rollback may require manual
recovery; previous backups are not erased at the start of an attempt.

`Applying` is published only after updater launch succeeds. Portable `config.json`
stays beside the application and is preserved through updates and rollback; logs
remain in the user's directory. Installed builds keep per-user configuration even
when updated using the portable ZIP. System packages/protected folders remain link-only;
the Linux Vulkan layer must still be reinstalled separately.

## Asset convention and publishing (maintainers)

Releases are generated locally (no publishing CI); the updater looks for these exact names:

The Windows ZIP and Linux tar.gz include `phantom.portable`, never a personal `config.json`. The Windows installer excludes that marker and the `.deb` removes it; both include `phantom.installed`. AppImage uses the `APPIMAGE` environment variable to locate the file outside its mounted image.

| Asset | Used by |
|---|---|
| `PhantomMonitor-<version>-win-x64.zip` | Automatic (Windows applier) + portable |
| `PhantomMonitor-<version>-linux-x64.tar.gz` | Automatic (portable Linux applier) + portable |
| `PhantomMonitor-<version>-linux-x64.AppImage` | Automatic (AppImage applier) |
| `PhantomMonitorSetup-<version>.exe` | Per-user installer (Inno Setup; files match the swapped folder) |
| `phantom-monitor_<version>_amd64.deb` | .deb install (stays in notify mode) |
| `PhantomVulkanLayer-<version>-linux.so` | Vulkan layer (manual; update by re-running that version's `install_layer.sh`) |

Publishing flow:

1. `scripts/release.ps1 -Version <x.y.z>`: publishes the unified app (win-x64 and linux-x64) self-contained single-file with `-p:Version=<version>` and generates the zip and tarball under `releases/<version>/` — the Windows payload is `PhantomMonitor.exe` + `PresentMon.exe` + `config.example.json` (the script guarantees a loose PresentMon and strips the `*.pdb`s) and the Linux one is the binary plus the transitional hardlink. Options: `-PackWindowsSetup` (installer when Inno Setup 6 is present), `-LayerSoPath <so>` (attaches the compiled Vulkan layer), `-SkipWindows/-SkipLinux` and `-OutDirectory`.
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
