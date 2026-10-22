> **Language:** English · [Português (Brasil)](../osd-in-frame-estudo.md)

# Windows in-frame OSD — architecture study (P4)

**Status:** architecture study only. No native DLL, hook, or configuration schema was added. The decision is documented with anti-cheat safety and the project's no-injection policy in mind.

## The problem

The current WPF `OverlayWindow` is a separate layered desktop window. It appears in desktop/window/display capture, but not in OBS game capture, which records the game's swapchain and excludes windows from other processes. An in-frame overlay would be rendered into frames presented by the game.

## Options considered

| Option | Risk / result | Decision |
|---|---|---|
| Injected DLL hooking `Present`/DXGI | High anti-cheat risk; typical signature of game cheats | Rejected |
| Injectable Windows Vulkan layer | Same process-injection risk plus graphics API maintenance | Rejected |
| DirectComposition or DWM hardware overlay plane | Does not enter the game's swapchain; does not solve game capture | Rejected |
| OBS plugin consuming the telemetry SDK | Reads shared telemetry outside the game and renders in OBS capture | Recommended |

## Recommended design: OBS plugin and telemetry SDK

The shared-memory SDK already publishes `phantom_monitor_telemetry_v1`, stable `Category`/`Category#index` keys, units, and hardware labels. A separate C/C++ plugin could read the map on a dedicated thread using the SDK reader's magic/version/seqlock checks, cache the latest snapshot, and render a custom `obs_source` in OBS's scene pipeline. It could reuse `overlay.shownSeries` and family ordering for consistent layout. The plugin would be maintained and distributed separately; the monitor would not need to inject into games.

An initial text-only source was estimated at roughly one week: shared-memory reader, OBS source and text layout, then optional frametime graph support in a later version.

The proposed implementation stages are: (1) a C++ reader following the sample `TelemetrySdkReader` sequence of checking magic, version, and a stable even seqlock value; (2) an OBS source that formats and draws text metrics in the render callback; and (3) a later frametime graph. The overlay would use family tags such as `GPU1` and `FAN2` with monospaced columns. The graph was intentionally left for a second version so the first deliverable could focus on text.

## Why hooks and injection are excluded

Anti-cheat systems may restrict foreign DLLs and Present hooks because those techniques resemble game cheats. Avoiding injection keeps the monitor usable without modifying the game process. Linux FPS uses a different, opt-in Vulkan-layer architecture that publishes frame timings to per-process POSIX shared memory; it does not inject a DLL. Porting that model to Windows is not planned because there is no equivalent safe layer mechanism without injecting into the target process.

This document records a possible OBS integration only. It does not change current product behavior; the existing telemetry SDK is the proposed contract.

The WPF desktop overlay remains the default external window and does not carry the anti-cheat risk of a hook. No native library, injection path, or dead configuration setting was introduced by this study.
