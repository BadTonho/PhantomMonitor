> **Language:** English · [Português (Brasil)](../roadmap-paridade.md)

# Monitoring feature-parity roadmap

This document records feature comparisons and implementation decisions for Phantom Monitor. It reflects the repository's late-2026 development history; completed items are historical status, not a promise of identical Windows and Linux interfaces.

## Capabilities already delivered

- Per-frame frametime through Windows ETW/PresentMon and the Linux Vulkan layer.
- 1%/0.1% lows, min/max/average, and 600-sample frametime history.
- Configurable alerts, including per-instance targets.
- Configurable OSD, profiles, hotkey, and tray integration on Windows.
- CSV/JSON logging and error log.
- Current builds do not load game-process hooks; Linux FPS uses an opt-in implicit Vulkan layer. The accepted in-game OSD design adds an explicit Windows executable allowlist and anti-cheat warning before hooks ship.

## P1 — Dynamic sensor series (completed)

`SensorKey` and `SensorData.Series/SeriesLabels` established the `Category#index` model while preserving canonical series. Implemented multi-GPU, per-core CPU, fan slots, per-drive metrics, VRM/rail sensors, CPU power domains, network-per-interface and session totals, inventory, OSD series allowlists, and Linux per-core/device/interface/card series.

## P2 — Shared-memory telemetry SDK (completed on Windows/WPF)

The retired WPF/Windows frontend published the versioned `phantom_monitor_telemetry_v1` map: a header and up to 256 fixed-size series entries per snapshot, with a seqlock protocol and a reference reader in `tools/TelemetrySdkReader/`. The SDK was retired with the WPF in Phase 2 of the visual unification (`6c4d2c0`); the unified app serves external tools through the opt-in HTTP exporter (`/values`, `/health`) on both systems, and Linux also uses separate per-process POSIX shared memory for Vulkan FPS timings. JSON remains a file-based fallback. See [Telemetry SDK](telemetry-sdk.md).

## P3 — Per-application OSD profiles (completed on Windows)

Profiles can override position, size, font, opacity, series, accent, and visibility based on the focused executable. The Windows editor manages profile cards; the tray can pin one for the session or return to automatic matching. Active profiles are indicated in the OSD footer.

## P4 — In-game OSD (renderers implemented; validation pending)

The product offers floating windows and an opt-in in-game mode. Linux Vulkan and GLX/EGL, plus Windows Direct3D 9–12, WGL, and Vulkan renderers are implemented. Windows hooks are limited to user-allowlisted executables, with an anti-cheat warning and no bypass. Fullscreen, resize, game-capture, and Windows runtime validation remains pending. See [in-frame OSD study](osd-in-frame-study.md).

## P5 — Hardware control (out of scope)

Overclocking, fan curves, and limiters require writes through vendor APIs or drivers. The product remains read-only; any future control feature should be a separate app or package.

## P6 — Instrumentation parity (completed)

- **P6.1:** per-series session min/max/average, reset from tray/inventory, CSV marker.
- **P6.2:** hide the OSD for a matching application with `profiles[].visible: false`.
- **P6.3:** optional HTTP exporter with `/values` and `/health`; loopback by default, all interfaces opt-in.
- **P6.4:** battery series on Windows and Linux; desktops without a battery publish none.
- **P6.5:** Windows PresentMon GPU Busy, CPU Busy, CPU Wait, present latency, and FPS/frametime P1/P99 where the installed output provides those columns. The Linux Vulkan reader does not currently publish these series.
- **P6.6:** OSD templates with key, decimal-format, and wildcard placeholders.
- **P6.7:** pause/resume through the Windows tray and global hotkey; telemetry freezes at the last snapshot and pause state is runtime-only.

## P8 — Additional reference-suite gaps (completed)

- **P8.1:** user formulas in `customSensors`, compiled into `Custom#i` series and included in statistics/exports/alerts.
- **P8.2:** Windows session WHEA event count.
- **P8.3:** append-only JSONL journal with metadata, samples, and session notes.
- **P8.4:** active monitor identity from EDID/WMI in inventory and JSON snapshots.
- **P8.5:** per-series OSD sparklines with individual history windows and automatic or fixed axes.
- **P8.6:** separate GPU memory junction series, `GpuVramTemperature`; GPU VRM/liquid/PLX diodes are not mixed into core temperature.

These capabilities correspond to custom sensor formulas, Windows WHEA event counting, continuous JSONL journaling, EDID/WMI monitor inventory, OSD per-series history graphs, and separate GPU memory-junction reporting. P8.1–P8.4 closed the most practical gaps identified in the RTSS/HWiNFO comparison; P8.5 and P8.6 followed as additional OSD and sensor refinements.

## P9 — Documentation audit and per-series tuning (completed)

- **P9.0:** documentation audit against implementation and resulting corrections.
- **P9.1:** `sensorTuning` controls series publication interval and moving-average smoothing. Repeated values do not count toward statistics; the last matching rule wins.

## Deliberate non-goals and remaining gaps

The project does not plan a Vulkan shared-memory seqlock at this time, placeholder values before first readings, or a different definition for 1%/0.1% lows with small sample counts. Bigger hardware-engine gaps include raw SMART attributes, vendor-specific GPU throttle reasons, CPU C-states/effective clocks, NPU sensors, detailed PCIe data, and third-party PSU/fan-controller ecosystems; they need vendor APIs, direct drivers, or sources not currently available through LibreHardwareMonitor.

## Remaining comparison notes

| Area | Status / decision |
|---|---|
| In-frame game capture | Opt-in in-game mode with Windows executable allowlist and no anti-cheat bypass. Shared-memory v1 and Linux/Windows native renderers are implemented; API-by-API runtime validation is pending. See [in-frame OSD study](osd-in-frame-study.md). Frame limiting remains out of scope. |
| Fan curves, voltage/frequency tuning, and overclocking | Out of scope for the read-only monitor. |
| PresentMon metrics (Windows) | Busy/wait, present latency, and P1/P99 are published when present in the installed PresentMon output; the Linux reader does not publish them. Mode-of-present/click-to-photon are not planned. |
| Rich OSD markup, conditional layers, bars, and formulas | Templates cover basic text and resolved-series conditions; more advanced layout remains a UI project. |
| External LCDs and remote dashboards | The open SDK and opt-in HTTP exporter provide data; third-party integrations remain external. |
| VR overlay | Niche and not planned. |
| Session stats and pause controls | Delivered in P6.1 and P6.7. |
| Battery, WHEA, custom formulas, and JSONL | Delivered in P6.4, P8.2, P8.1, and P8.3. |
| OSD graphs and monitor identity | Delivered in P8.5 and P8.4; HDR/VRR metadata remains unimplemented. |
| Per-sensor polling/smoothing | Delivered in P9.1; physical reads still occur on the global tick. |
| Concurrent polling | Not enabled; LibreHardwareMonitor is sequential, and parallel driver access needs concurrency validation. |

Additional distinctions from the reference suites:

- NVIDIA Reflex latency markers (input-to-GPU-render) rely on game-process hooks and remain excluded by policy.
- Rich hypertext tags, time bands, conditional layers, macros, and expression/filter functions are not implemented; current templates resolve telemetry placeholders and simple missing-value visibility.
- Email/webhook destinations are not built in. Alert scripts can implement external delivery.
- External LCD integrations can use the SDK but remain separate products or plugins.
- Full SMART attribute tables (for example, reallocated sectors and media errors), GPU power-limit reasons, CPU C-state residency/effective clocks, NPU data, VBIOS/ASIC details, PCIe counters, SPD timings, vendor PSU controllers, and ARM/Snapdragon platform data require sources beyond the current sensor engine.
- HDR/VRR monitor metadata requires a fuller display descriptor; the current EDID work reports manufacturer/model/serial, diagonal, manufacture date, and primary status.

## Project strengths

The retired WPF shared-memory SDK was open and documented, CSV columns grow dynamically with instance series, and the design avoids hooks into game processes. The unified app offers the opt-in HTTP exporter for full sensor snapshots on both systems and uses the POSIX shared-memory channel only for Vulkan frame timings on Linux. These choices keep telemetry available to external tools without granting them access to the game's address space.

The implementation order recorded in the original roadmap prioritized session statistics/reset, per-app hide, HTTP export, battery, PresentMon metrics, templates, and pause/resume (P6.1–P6.7), followed by custom sensors, WHEA, JSONL, EDID, OSD graphs, GPU memory junction, and per-series tuning. The next larger work items remain vendor-dependent hardware sources rather than missing basic monitoring controls.
