> **Language:** English · [Português (Brasil)](../../funções/fps-monitor.md)

# FPS and frametime monitoring

On Windows, FPS collection uses Intel PresentMon and ETW without injecting a DLL. On Linux, it observes X11 Present events from X11/XWayland windows and uses Vulkan telemetry plus passive EGL/GLX hooks as additional sources. These hooks record frames without drawing the in-game overlay, but they must be loaded when the game starts.

## Windows: PresentMon and ETW

The unified app starts PresentMon 2.x on Windows and reads its CSV stream. It identifies the foreground game using Win32 APIs and consumes frametime and optional PresentMon metrics. PresentMon requires an elevated session or membership in **Performance Log Users**. The app does not request elevation automatically. If ETW session startup is denied, `EtwDiag` gives this guidance and `logs/errors.log` receives one actionable entry for that PresentMon session; `warning` lines are not logged as errors. The app requests only options supported by the installed PresentMon version.

## Linux: X11 Present and Vulkan

The floating monitor observes X11 Present completion events for X11/XWayland windows and prioritizes the focused window. If PhantomMonitor has focus, it selects another window that is presenting frames. This source requires the X11 Present extension and `libxcb-present`; native Wayland windows do not expose these events. The app also prepares passive Vulkan and EGL/GLX hooks for telemetry in both overlay modes. They publish frame data without drawing the in-game overlay. Games and launchers already running must restart after activation.

For Vulkan titles, the app reads `PhantomVulkanLayer` after the launch environment is activated, regardless of whether the overlay is floating or in-game. It intercepts `vkQueuePresentKHR` and records frame timing in per-process POSIX shared memory. OpenGL GLX/EGL uses the passive preload for the same purpose. Existing game processes and launchers must restart after activation.

X11 Present timestamps measure completed presentations on the X server. Vulkan and EGL/GLX hooks use monotonic timestamps and write frame timing to shared memory with minimal work on the present path. The reader computes an exponential moving average, rolling statistics, and a 600-sample frametime history. Shared-memory objects are scoped to each game process and removed when the hook is unloaded; they fall back to `/tmp/phantom_fps_<pid>.bin` if `/dev/shm` is unavailable.

## Published values

Both frontends expose current FPS and frametime, rolling average/minimum/maximum, 1% and 0.1% lows, and a 600-sample frametime history used by the dashboard and overlay graphs. P1/P99 percentiles and the optional GPU Busy, CPU Busy, CPU Wait, and present-latency series are provided by the Windows PresentMon reader only, when the installed PresentMon output contains the required columns. They are not published by the Linux Vulkan reader.

The Windows rolling window holds up to 3600 frame samples (about 60 seconds at 60 FPS). FPS is smoothed with an exponential moving average; Windows percentiles are calculated over the recent window. The Linux monitor calculates its rolling average, minimum, maximum, 1%/0.1% lows, and frametime history from X11 Present, Vulkan, or EGL/GLX timestamps; it does not currently calculate P1/P99 series.

On Windows, optional CSV columns are matched case-insensitively by substring because PresentMon versions use names such as `GpuBusy%`, `GPU Busy %`, `MsUntilDisplayed`, or `PresentLatency`. Busy and wait percentages are accepted only from 0 to 100%; latency is constrained to a plausible range of 0–2000 ms. If the installed PresentMon does not provide a column, its series is unavailable (some older versions publish zero, which consumers treat as unavailable for these optional metrics). `FpsP1` uses the same rolling-window percentile as `Fps1Low`; P99 FPS and frametime expose the other tail of their respective windows.

The WPF service parses quoted CSV fields dynamically, falls back among the available frametime columns, and checks the foreground process periodically. Linux Diagnostics reports the X11/XWayland process being observed, whether frames have arrived, or why X11 Present is unavailable; Vulkan and EGL/GLX telemetry are complementary sources.

## Linux Vulkan telemetry v2
The native layer publishes a sequence-protected 4,096-frame ring with a session
identifier. Polling drains every retained frame, including spikes between polls;
overflow is reported. Statistics use up to 3,600 actual frames. Idle entries older
than two seconds cannot block an active game. PID/session/counter restarts reset
all metrics; stale readings clear `HasData`. Legacy v1 remains readable with an
approximation warning. Release packages update the layer with the app; manual
installations made with `install_layer.sh` must rerun that script after updating.
The v2 layout is 98,424
bytes: legacy prefix 100, capacity at 100, uint64 sequence at 104, uint64 session
at 112, and 4,096 24-byte frame records at 120 (number, timestamp, float ms/FPS).
