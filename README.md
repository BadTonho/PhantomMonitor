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
  <img src="https://img.shields.io/badge/Windows-WPF-0078D4?logo=windows&logoColor=white" alt="Windows with WPF" />
  <img src="https://img.shields.io/badge/Linux-Avalonia-FCC624?logo=linux&logoColor=111111" alt="Linux with Avalonia" />
</p>

<p align="center">
  <a href="#features">Features</a> ·
  <a href="#getting-started">Getting started</a> ·
  <a href="#architecture">Architecture</a> ·
  <a href="#documentation">Documentation</a>
</p>

**Phantom Monitor** reads hardware sensors and displays live telemetry for your computer and games. It brings utilization, temperatures, clocks, memory, storage, network, fans, FPS, and frametime into one desktop app.

The project has two frontends: **WPF on Windows** and **Avalonia on Linux**. Their feature sets differ today; the table below summarizes the current platform support.

## Features

| Feature | Windows · WPF | Linux · Avalonia |
| --- | --- | --- |
| Sensors | CPU, GPU, RAM, drives, network, fans, and motherboard sensors | Sensors from `sysfs`, `procfs`, `hwmon`, DRM, and NVIDIA NVML when its driver library is installed |
| FPS and frametime | PresentMon through ETW | Phantom Vulkan layer for Vulkan games |
| Overlay | Advanced OSD, click-through, profiles, and visual editor | Configurable floating window; the compositor controls position and fullscreen behavior |
| Inventory | Hardware inventory and per-series statistics | Detected series by category; expected sensors without readings are marked unavailable |
| Alerts | Notifications, sound, scripts, and configurable shutdown action | In-app toast, desktop notifications, and scripts |
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

Run the WPF frontend:

```powershell
dotnet run --project src/PhantomMonitor
```

Some hardware readings may require elevated permissions. FPS collection uses PresentMon/ETW and may depend on the Windows version and available permissions.

## Configuration and data

On first launch, the app creates `config.json` from [`config.example.json`](config.example.json). During development, this file is placed in the repository root. For per-user installations, the usual locations are `%AppData%\PhantomMonitor\config.json` on Windows and `$XDG_CONFIG_HOME/PhantomMonitor/config.json` on Linux.

On Linux, telemetry logs are stored in `~/.local/share/PhantomMonitor/logs/` (or under `$XDG_DATA_HOME` when set). The Avalonia **Settings** page can change the sampling interval, logging formats, and overlay options. The HTTP exporter is disabled by default and listens on loopback; enabling other network interfaces exposes its endpoints without TLS or authentication.

## Architecture

```text
src/
├── PhantomMonitor.App/   # Avalonia frontend for Linux
├── PhantomMonitor.Core/  # Shared .NET models and services
├── PhantomMonitor/       # WPF frontend for Windows
├── PhantomVulkanLayer/   # Native Vulkan layer for Linux FPS telemetry
└── PhantomSensorLib/     # Native C++ library reserved for future work
```

The Core shares models, configuration, alert evaluation, and telemetry services. Each frontend keeps its operating-system integrations. The Vulkan layer is built separately with CMake and `install_layer.sh`; it is not part of the .NET solution build.

## Documentation

The documentation is available in both English and Brazilian Portuguese. English is the default; each guide links to its counterpart.

- [English documentation index](docs/en/README.md)
- [Índice da documentação em português (Brasil)](docs/README.md)

## Project status

Avalonia is the current Linux frontend. On Windows, WPF remains the primary app; available interface features and actions vary by platform. See the [roadmap](docs/en/feature-parity-roadmap.md) for the ongoing migration.

## License

Phantom Monitor's original code is licensed under the GNU Lesser General Public License v3.0 only (`LGPL-3.0-only`). See [LICENSE](LICENSE). Third-party components keep their own licenses; notices for the bundled PresentMon executable are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
