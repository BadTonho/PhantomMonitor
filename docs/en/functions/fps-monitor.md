> **Language:** English · [Português (Brasil)](../../funções/fps-monitor.md)

# FPS and frametime monitoring

FPS collection is platform-specific and does not inject a DLL into the game. Separately, the opt-in in-game overlay installs hooks in processes observed by PresentMon; anti-cheat software may block this integration, and the app does not try to bypass those blocks.

## Windows: PresentMon and ETW

The unified app starts PresentMon 2.x on Windows and reads its CSV stream. It identifies the foreground game using Win32 APIs and consumes frametime and optional PresentMon metrics. PresentMon requires an elevated session or membership in **Performance Log Users**. The app does not request elevation automatically. If ETW session startup is denied, `EtwDiag` gives this guidance and `logs/errors.log` receives one actionable entry for that PresentMon session; `warning` lines are not logged as errors. The app requests only options supported by the installed PresentMon version.

## Linux: Phantom Vulkan layer

The unified (Avalonia) frontend reads telemetry published by `PhantomVulkanLayer`, a native implicit Vulkan layer that intercepts `vkQueuePresentKHR` and records frame timing in POSIX shared memory. The layer is discovered by the Vulkan loader, but publishes data only when `ENABLE_PHANTOM_OVERLAY=1` is set for the game. `DISABLE_PHANTOM_OVERLAY=1` overrides the enable switch. See [Linux support](linux-support.md) for installation and Steam launch options.

The Linux reader matches shared-memory objects to live process IDs and ignores stale objects. FPS requires a Vulkan title; non-Vulkan games do not publish frames through this layer.

The layer keeps a monotonic timestamp for each present and writes frame timing to shared memory with minimal work on the present path. The reader computes an exponential moving average, 1%/0.1% lows, maximum, and a 600-sample frametime history. The shared-memory object is scoped to the game process, removed when the Vulkan instance is destroyed, and falls back to `/tmp/phantom_fps_<pid>.bin` if `/dev/shm` is unavailable.

## Published values

Both frontends expose current FPS and frametime, rolling average/minimum/maximum, 1% and 0.1% lows, and a 600-sample frametime history used by the dashboard and overlay graphs. P1/P99 percentiles and the optional GPU Busy, CPU Busy, CPU Wait, and present-latency series are provided by the Windows PresentMon reader only, when the installed PresentMon output contains the required columns. They are not published by the Linux Vulkan reader.

The Windows rolling window holds up to 3600 frame samples (about 60 seconds at 60 FPS). FPS is smoothed with an exponential moving average; Windows percentiles are calculated over the recent window. The Linux monitor calculates its rolling average, minimum, maximum, 1%/0.1% lows, and frametime history from Vulkan frame timestamps; it does not currently calculate P1/P99 series.

On Windows, optional CSV columns are matched case-insensitively by substring because PresentMon versions use names such as `GpuBusy%`, `GPU Busy %`, `MsUntilDisplayed`, or `PresentLatency`. Busy and wait percentages are accepted only from 0 to 100%; latency is constrained to a plausible range of 0–2000 ms. If the installed PresentMon does not provide a column, its series is unavailable (some older versions publish zero, which consumers treat as unavailable for these optional metrics). `FpsP1` uses the same rolling-window percentile as `Fps1Low`; P99 FPS and frametime expose the other tail of their respective windows.

The WPF service parses quoted CSV fields dynamically, falls back among the available frametime columns, and checks the foreground process periodically. The Linux dashboard displays a clear prompt to enable the Phantom layer when no Vulkan frames have arrived.

## Linux Vulkan telemetry v2
The native layer publishes a sequence-protected 4,096-frame ring with a session
identifier. Polling drains every retained frame, including spikes between polls;
overflow is reported. Statistics use up to 3,600 actual frames. Idle entries older
than two seconds cannot block an active game. PID/session/counter restarts reset
all metrics; stale readings clear `HasData`. Legacy v1 remains readable with an
approximation warning. Reinstall the layer after updating. The v2 layout is 98,424
bytes: legacy prefix 100, capacity at 100, uint64 sequence at 104, uint64 session
at 112, and 4,096 24-byte frame records at 120 (number, timestamp, float ms/FPS).
