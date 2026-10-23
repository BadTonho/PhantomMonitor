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
| FPS and frametime | PresentMon through ETW | Phantom Vulkan layer for Vulkan games |
| Overlay | Advanced OSD, click-through, and profiles (editing via config.json) | Configurable floating window; the compositor controls position and fullscreen behavior |
| Inventory | Hardware inventory and per-series statistics | Detected series by category; expected sensors without readings are marked unavailable |
| Alerts | Notifications, sound, scripts, and configurable shutdown action | In-app toast, desktop notifications, and scripts |
| Diagnostics | Sensor sources, current GPU warnings, and application error log | Per-GPU driver/source status, current GPU warnings, recent errors, and NVIDIA NVML details |
| Formulas and per-series tuning | Compatible with the existing configuration format | Visual editors for custom sensors and sampling/smoothing rules |
| Telemetry | CSV, JSON, JSONL, and optional HTTP exporter | CSV, JSON, JSONL, and optional HTTP exporter |

Hardware and compositor support varies between systems. On Linux, FPS requires a Vulkan game and the Phantom layer enabled. On Windows, FPS collection uses ETW; the monitor does not inject DLLs into games.

## Getting started

Install the **.NET 10 SDK**. For platform-specific requirements, see the [Linux support guide](docs/en/functions/linux-support.md) and [configuration guide](docs/en/functions/configuration.md).

### Linux

Run the Avalonia dashboard:

```bash
dotnet run --project src/PhantomMonitor.App
```

To collect FPS from Vulkan games, build and install the Phantom layer:

```bash
cd src/PhantomVulkanLayer
chmod +x install_layer.sh
./install_layer.sh
```

Then add this to the game's launch options, for example in Steam:

```bash
ENABLE_PHANTOM_OVERLAY=1 %command%
```

The dashboard and Linux overlay work without the layer when FPS is not needed. On Wayland, the compositor controls the overlay's initial position and whether it appears above fullscreen applications.

### Windows

Run the app:

```powershell
dotnet run --project src/PhantomMonitor.App
```

Pick the built-in theme with `theme` in `config.json` (`neptune` = original Avalonia look, `slate` = verbatim port of the WPF look; the legacy `uiTheme` key still loads and migrates), through the **INTERFACE** card in Settings (applied to the running UI without a restart) or with `--theme=<name>` (legacy `--ui-theme=` accepted).

Local release build of the Windows binary:

```powershell
dotnet publish src/PhantomMonitor.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o releases/win-x64
```

The output carries `PresentMon.exe` and `config.example.json` alongside the executable.

Some hardware readings may require elevated permissions. FPS collection uses PresentMon/ETW and may depend on the Windows version and available permissions.

## Releases and auto-update

Published builds live on the [Releases page](https://github.com/BadTonho/PhantomMonitor/releases). The app checks for newer versions at startup and every 24h and offers "Atualizar e reiniciar" (Update and restart): it downloads the platform package, swaps the files through a one-shot updater (with backup/rollback) and relaunches on its own — config and logs live outside the swapped folder. System-package installs (.deb) and protected folders stay in notify-with-link mode. Each release follows the convention:

| Asset | Platform |
| --- | --- |
| `PhantomMonitor-<version>-win-x64.zip` | Windows (portable; also the installer payload) |
| `PhantomMonitor-<version>-linux-x64.tar.gz` | Linux (portable) |
| `PhantomMonitor-<version>-linux-x64.AppImage` | Linux (AppImage) |
| `PhantomMonitorSetup-<version>.exe` | Windows (per-user installer via Inno Setup) |
| `phantom-monitor_<version>_amd64.deb` | Linux (.deb package) |
| `PhantomVulkanLayer-<version>-linux.so` | Vulkan layer (manual install with `install_layer.sh`; not part of the automatic swap) |

Releases are generated locally by maintainers (`scripts/release.ps1`, no CI); see the [updates guide](docs/en/functions/updates.md) and the packagers under `packaging/`. Because the packages are standalone binaries signed by us, Windows may show the **SmartScreen** warning ("unknown publisher") and Linux may ask for confirmation on the first launch.

## Configuration and data

On first launch, the app creates `config.json` from [`config.example.json`](config.example.json). During development, this file is placed in the repository root. For per-user installations, the usual locations are `%AppData%\PhantomMonitor\config.json` on Windows and `$XDG_CONFIG_HOME/PhantomMonitor/config.json` on Linux.

On Linux, telemetry and error logs are stored in `~/.local/share/PhantomMonitor/logs/` (or under `$XDG_DATA_HOME` when set). Open **Diagnostics** in Avalonia to inspect recent errors and the detected sensor sources. The **Settings** page can change the sampling interval, logging formats, and overlay options. The HTTP exporter is disabled by default and listens on loopback; enabling other network interfaces exposes its endpoints without TLS or authentication.

## Architecture

```text
src/
├── PhantomMonitor.App/   # Unified Avalonia desktop app (Windows + Linux)
├── PhantomMonitor.Core/  # Shared .NET models and services
├── PhantomVulkanLayer/   # Native Vulkan layer for Linux FPS telemetry
└── PhantomSensorLib/     # Native C++ library reserved for future work
```

The Core shares models, configuration, alert evaluation, and telemetry services; the app keeps the operating-system-specific integrations. The Vulkan layer is built separately with CMake and `install_layer.sh`; it is not part of the .NET solution build.

## Documentation

The documentation is available in both English and Brazilian Portuguese. English is the default; each guide links to its counterpart.

- [English documentation index](docs/en/README.md)
- [Índice da documentação em português (Brasil)](docs/README.md)

## Project status

The app is the **single Avalonia desktop frontend on both Windows and Linux**; available interface features vary by operating system (sensors, FPS, and OSD integrations). See the [roadmap](docs/en/feature-parity-roadmap.md) for the ongoing platform work.

## License

Phantom Monitor's original code is licensed under the GNU Lesser General Public License v3.0 only (`LGPL-3.0-only`). See [LICENSE](LICENSE). Third-party components keep their own licenses; notices for the bundled PresentMon executable are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
