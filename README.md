<p align="right">English | <a href="README.pt-BR.md">Português (Brasil)</a></p>

<p align="center">
  <img src="fantasma.png" alt="Temporary Phantom Monitor project icon" width="170" />
</p>

<h1 align="center">Phantom Monitor</h1>

<p align="center">
  <strong>Hardware, FPS, and frametime in one place.</strong><br />
  Local monitoring for Windows and Linux, with a desktop dashboard and configurable overlay.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Windows-Avalonia-0078D4?logo=windows&logoColor=white" alt="Windows with Avalonia" />
  <img src="https://img.shields.io/badge/Linux-Avalonia-FCC624?logo=linux&logoColor=111111" alt="Linux with Avalonia" />
</p>

<p align="center">
  <a href="#features">Features</a> ·
  <a href="#getting-started">Getting started</a> ·
  <a href="#architecture">Architecture</a> ·
  <a href="#documentation">Documentation</a>
</p>

**Phantom Monitor** reads hardware sensors and displays live telemetry for your computer and games. It brings utilization, temperatures, clocks, memory, storage, network, fans, FPS, and frametime into one desktop app.

The project runs a **single Avalonia desktop app** on Windows and Linux. Earlier releases shipped a WPF frontend on Windows, retired in phase 2 of the [visual unification roadmap](docs/en/visual-unification-roadmap.md); the table below summarizes the current platform support.

## Features

| Feature | Windows | Linux |
| --- | --- | --- |
| Sensors | CPU, GPU, RAM, drives, network, fans, and motherboard sensors via LibreHardwareMonitor | Sensors from `sysfs`, `procfs` (including DRM `fdinfo`), `hwmon`, Linux thermal zones, DRM, and optional NVIDIA NVML; GPU metrics vary by driver |
| FPS and frametime | PresentMon through ETW | X11 Present for X11/XWayland windows, plus passive Vulkan and OpenGL GLX/EGL frame capture |
| Overlay | Floating OSD with click-through and profiles; opt-in in-game renderers for frame-presenting applications: Direct3D 9–12, OpenGL/WGL, and Vulkan through a per-user implicit layer | Separate draggable windows; opt-in in-game rendering through Vulkan and OpenGL GLX/EGL; Linux releases bundle both native renderers |
| Inventory | Hardware inventory and per-series statistics | Detected series by category; expected sensors without readings are marked unavailable |
| Alerts | Notifications, sound, scripts, and configurable shutdown action | In-app toast, desktop notifications, and scripts |
| Diagnostics | Sensor sources, current GPU warnings, and application error log | Per-GPU source status, overlay window geometry, current GPU warnings, recent errors, and NVIDIA NVML details |
| Formulas and per-series tuning | Compatible with the existing configuration format | Visual editors for custom sensors and sampling/smoothing rules |
| Telemetry | CSV, JSON, JSONL, and optional HTTP exporter | CSV, JSON, JSONL, and optional HTTP exporter |

Hardware, graphics API, and compositor support varies between systems. Linux releases bundle the Vulkan layer and GLX/EGL preload. Floating mode enables passive frame telemetry without drawing in the game; in-game mode also enables the OSD renderer. Both rely on launch-environment integration, so games and launchers already running must be restarted after activation. On KDE Plasma, the app tries the session launch service and KLauncher where available; Diagnostics reports whether a desktop service confirmed the update. If no service confirms it, applications started by the desktop may not inherit the hook during the current session. The persistent setting prepares future sessions. X11 Present remains available for X11/XWayland windows; native Wayland windows require a loaded Vulkan or OpenGL hook. The `phantom-hud` wrapper CLI (in `~/.local/bin/phantom-hud`) allows launching games and launchers directly (e.g. `phantom-hud %command%` in Steam or as a wrapper command in Prism Launcher, MultiMC, Heroic, and Lutris). Flatpak may need separate integration. Windows discovers presenting applications through PresentMon/ETW and supports early launching from Settings. D3D12 discovery captures an unambiguous DIRECT queue; games using multiple queues should be launched through Settings to capture the exact swapchain association. ETW may require membership in Performance Log Users. The app does not elevate itself or bypass anti-cheat protections. Native Windows builds require Visual Studio 2022 C++ tools and Vulkan headers; loader import libraries are not required. Elevated Vulkan applications may ignore per-user registrations. Game/API runtime compatibility still requires validation.

## Getting started

Install the **.NET 10 SDK**. For platform-specific requirements, see the [Linux support guide](docs/en/functions/linux-support.md) and [configuration guide](docs/en/functions/configuration.md).

### Linux

Run the Avalonia dashboard:

```bash
dotnet run --project src/PhantomMonitor.App
```

For a local development build, compile the native overlay payload into the app's output folder:

```bash
scripts/build-linux-overlay.sh src/PhantomMonitor.App/bin/Debug/net10.0/PhantomOverlay/linux
dotnet run --project src/PhantomMonitor.App
```

Release packages already include the Vulkan and OpenGL renderers. On Linux, frame capture is prepared in both overlay modes. The app registers the Vulkan layer for passive telemetry and publishes the GLX/EGL preload to D-Bus/systemd; on KDE Plasma, it also tries the session launch service and KLauncher where available. Select **In-game** only when you also want the OSD drawn in the game. Diagnostics reports when the desktop does not confirm its launch environment, in which case games started from its menu may not inherit the hooks during this session. Close and reopen games or launchers that were already running. On Windows, the executable list remains available for launching games with specific parameters.

For maintainers producing a release, build this payload on Linux, make the folder available on the Windows release machine, and pass it to the release script:

```bash
scripts/build-linux-overlay.sh build/linux-overlay-payload
```

On the Windows release machine, after making that folder available:

```powershell
powershell -File scripts/release.ps1 -Version 1.2.3 -LinuxOverlayDirectory .\build\linux-overlay-payload
```

Vulkan and OpenGL GLX/EGL provide passive FPS telemetry in floating mode and can draw the in-game OSD when `inGame` is selected. OpenGL drawing supports EGL (OpenGL ES 3+) and one context per game process. In **Settings → Overlay mode**, `floating` remains the default and old configs migrate to it. Choosing `inGame` hides the floating windows; native per-process state and failures appear in Diagnostics. Vulkan presents with multiple swapchains and Windows DXGI processes with multiple swapchains are reported unsupported. On a Wayland desktop, check **Diagnostics → Summary** for the Avalonia backend: this project's default uses X11/XWayland, while a native Wayland compositor may ignore absolute window positions.

### Windows

Run the app:

```powershell
dotnet run --project src/PhantomMonitor.App
```

The overlay visibility hotkey can be captured in **Settings → Overlay mode**; it is disabled by default. Windows is supported, and X11/Wayland implementations await validation on Linux. See [global hotkeys](docs/en/functions/global-hotkey.md).

In the overlay editor, **Add content → Add custom line** combines free text with detected CPU, GPU, and motherboard names and supports per-line font size and width. See [overlay](docs/en/functions/overlay.md).

Pick the built-in theme with `theme` in `config.json` (`neptune` = Avalonia-derived palette, `slate` = WPF-derived palette; the legacy `uiTheme` key still loads and migrates), then customize its colors through **INTERFACE → Personalizar paleta** in Settings. Palette edits apply after **Salvar e aplicar** succeeds. The theme selector also works at runtime without restarting, and `--theme=<name>` is available for comparison (legacy `--ui-theme=` accepted).

The **INTERFACE** settings also switch navigation between the left sidebar, a horizontally scrollable bottom bar, and a floating dock centered over page content inside the main window. Choose labels with icons or icons only; the compact left sidebar is 76 px wide. These preferences save immediately as `navigationPosition` (`left` by default) and `navigationDisplayMode` (`iconsAndText` by default), including for older configs.

Local release build of the Windows binary:

```powershell
dotnet publish src/PhantomMonitor.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o releases/win-x64
```

The output carries `PresentMon.exe` and `config.example.json` alongside the executable.

The app's normal `dotnet build` and `dotnet publish` do not compile or copy the
Windows in-game renderer. For a local Windows build, run
`scripts/build-windows-overlay.ps1 -OutDirectory <app-folder>/PhantomOverlay`.
The release script does this automatically. It creates x86/x64 hooks, launchers,
and Vulkan layers. The script requires
Visual Studio 2022 C++ tools and Vulkan headers (from the Vulkan SDK).
Both architectures resolve Vulkan functions dynamically; SDK import libraries
are not required. Windows runtime validation is still
required for each API and for game capture.

Some hardware readings may require elevated permissions. FPS collection uses PresentMon/ETW and may depend on the Windows version and available permissions.

## Releases and auto-update

Published builds live on the [Releases page](https://github.com/BadTonho/PhantomMonitor/releases). The app checks for newer versions at startup and every 24h and offers "Atualizar e reiniciar" (Update and restart): it downloads the platform package, swaps the files through a one-shot updater (with backup/rollback) and relaunches on its own. Portable configuration is preserved during the swap; logs remain in the user's data directory. System-package installs (.deb) and protected folders stay in notify-with-link mode. Each release follows the convention:

| Asset | Platform |
| --- | --- |
| `PhantomMonitor-<version>-win-x64.zip` | Windows (portable; also the installer payload) |
| `PhantomMonitor-<version>-linux-x64.tar.gz` | Linux (portable) |
| `PhantomMonitor-<version>-linux-x64.AppImage` | Linux (AppImage) |
| `PhantomMonitorSetup-<version>.exe` | Windows (per-user installer via Inno Setup) |
| `phantom-monitor_<version>_amd64.deb` | Linux (.deb package) |
Releases are generated locally by maintainers (`scripts/release.ps1`, no CI); the Linux tarball bundles both native renderers. See the [updates guide](docs/en/functions/updates.md) and the packagers under `packaging/`. The scripts do not sign the binaries. Windows may show the **SmartScreen** warning ("unknown publisher") and Linux may ask for confirmation on the first launch. Automatic downloads require the SHA-256 digest published by the GitHub API. Updates include renderer subfolders, retain recovery backups and roll back the full changed set on a handled copy failure. Close games using the Windows hooks before updating.

## Regression checks

Linux tarballs are generated by `tools/ReleasePackager` with explicit POSIX modes,
so executables remain executable when releases are packaged on Windows.

Run `dotnet test` (or `dotnet test tests/PhantomMonitor.OverlayRegressionTests -c Release`) to execute the xUnit regression and unit test suite (`RegressionTests`, `AlertRuleEvaluatorTests`, `CustomSensorFormulaParserTests`, and `AppConfigNormalizeTests`). Native checks use `cmake -S src/PhantomVulkanLayer -B build/native -DBUILD_TESTING=ON`, `cmake --build build/native --config Release`, then `ctest --test-dir build/native -C Release --output-on-failure`. On Linux, run `sh tests/linux-regression.sh` for update/package checks. EGL/GLX development packages are required for the OpenGL preload; `-DPHANTOM_BUILD_OPENGL=OFF` builds Vulkan and its tests independently.

## Configuration and data

On first launch, the app creates `config.json` from [`config.example.json`](config.example.json). The Windows portable ZIP and Linux portable tar.gz keep it beside the executable; an AppImage keeps it beside the `.AppImage` file. Move this file with the application to keep its settings. The directory must be writable: the app reports failures instead of silently switching locations. An updated portable without a local configuration starts fresh without copying or deleting the computer's old configuration.

During development, the file is placed in the repository root. For the Windows installer and `.deb`, the usual per-user locations are `%AppData%\PhantomMonitor\config.json` on Windows and `$XDG_CONFIG_HOME/PhantomMonitor/config.json` on Linux. Logs remain in the user's data directories.

On Linux, telemetry and error logs are stored in `~/.local/share/PhantomMonitor/logs/` (or under `$XDG_DATA_HOME` when set). Open **Diagnostics** in Avalonia to inspect recent errors and the detected sensor sources. The **Settings** page can change the sampling interval, logging formats, and overlay options. The HTTP exporter is disabled by default and listens on loopback; enabling other network interfaces (`listenAll`) generates and requires an access token (`Authorization: Bearer <token>` or `?token=<token>`), though traffic remains plain HTTP without TLS.

## Architecture

```text
src/
├── PhantomMonitor.App/   # Unified Avalonia desktop app (Windows + Linux)
├── PhantomMonitor.Core/  # Shared .NET models and services
├── PhantomVulkanLayer/   # Native Vulkan layer for Linux FPS telemetry
└── PhantomSensorLib/     # Native C++ library reserved for future work
```

The Core shares models, configuration, alert evaluation, and telemetry services; the app keeps the operating-system-specific integrations. Linux native renderers are built separately with CMake by `scripts/build-linux-overlay.sh` and bundled into releases; they are not part of the .NET solution build.

## Documentation

The documentation is available in both English and Brazilian Portuguese. English is the default; each guide links to its counterpart.

- [English documentation index](docs/en/README.md)
- [Índice da documentação em português (Brasil)](docs/README.md)

## Project status

The app is the **single Avalonia desktop frontend on both Windows and Linux**; available interface features vary by operating system (sensors, FPS, and OSD integrations). See the [roadmap](docs/en/feature-parity-roadmap.md) for the ongoing platform work.

## License

Phantom Monitor's original code is licensed under the GNU Lesser General Public License v3.0 only (`LGPL-3.0-only`). See [LICENSE](LICENSE). Third-party components keep their own licenses; notices for the bundled PresentMon executable are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
