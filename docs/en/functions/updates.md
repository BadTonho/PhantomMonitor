# Updates (GitHub Releases)

> **Language:** English · [Português (Brasil)](../../funções/atualizações.md)

Phantom Monitor checks for newer versions published on GitHub Releases and notifies the UI. The check is shared by both frontends (implemented in `PhantomMonitor.Services.Update`, in Core); applying the update automatically (download + file swap) is platform-specific.

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
- **"Nova versão disponível" banner** in the main content when a newer version exists, with a "Ver no GitHub" button.
- **"Nova versão" badge** in the sidebar, next to the monitor state; clicking it opens the releases page.

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
