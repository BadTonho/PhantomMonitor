# In-game OSD — architecture and phases

> **Language:** English · [Português (Brasil)](../osd-in-frame-estudo.md)

## Decision

Phantom Monitor will offer two global modes: **Floating window**, which keeps
blocks in separate windows, and **In-game**, which draws the applied
presentation into the game's frame. Floating remains the default for new and
existing configurations. In-game is opt-in; when its renderer is unavailable or
fails, Diagnostics reports the reason and the app does not switch to windows.

On Windows, the custom hook will be limited to executables the user adds to
`overlay.inGameTargets`. EAC, BattlEye, Vanguard, and other anti-cheats may reject
or block hooks. Phantom Monitor will not bypass those protections. Floating mode
remains available in those cases.

## App-to-renderer contract

The app builds a presentation from **applied** content: block and item order,
current values, styles, graphs, templates, and positions. The editor preview
continues to show draft values. The active presentation is published through an
internal versioned shared-memory channel. Native readers have read-only access
and accept a frame only when its version, stable sequence, size, and timestamp
are valid. Partial, unknown, or stale frames must not be drawn. This is not a
public API.

The current v1 channel has a 32-byte header and a UTF-8 tab-delimited payload
with LF-terminated records. `F` contains payload version, visibility, pause,
width, reference area, label and value fonts, opacity, colors, border, margins,
padding, spacing, and frametime graph height. `B` contains a Base64 id,
normalized position, and Base64 title. `N`/`T` carry font size, color, and
Base64 text; `G` carries height, color, optional axis limits, and up to 1200
samples; `E` ends a block. The header
stores publication time and sequence. The app refreshes the timestamp every
second while this mode is active, including when monitoring is paused; readers
must reject publications older than three seconds. The POSIX name remains
available so an already-running game can receive frames after the app restarts;
normal shutdown publishes a hidden state. The app keeps
`/phantommonitor_overlay_v1_<uid>` read-only for other processes while writing
through its already-open mapping. A reader
that does not recognize the version must ignore the frame.

## Implementation matrix

| Phase | Linux | Windows |
|---|---|---|
| Base | Vulkan | Direct3D 11 and 12 |
| Expansion | OpenGL | OpenGL, Vulkan, and Direct3D 9 and 10 |
| Target | Vulkan and OpenGL | Direct3D 9–12, Vulkan, and OpenGL |

Each API needs its own presentation interception and lifecycle, swapchain or
context, resize, synchronization, capture, and failure handling. Vulkan layers
follow the frame-presentation interception approach used by MangoHud, while the
Phantom renderer will use its own protocol and resource handling. A hook for
one API does not imply support for another.

On Linux, users can start a configured executable from Phantom Monitor, which
sets `ENABLE_PHANTOM_OVERLAY=1`, or copy that variable into the game's Steam
launch options. Per-process gating also keeps the existing Vulkan FPS layer
available.

## Code status

Configuration, migration to `floating`, executable list, v1 publication of the
applied frame, Linux activation controls, and capability diagnostics are
implemented. **Native renderers are not implemented yet**: the current Vulkan
layer measures FPS/frametime but does not consume the frame to draw it; no
Direct3D hooks have been added. Therefore `inGame` does not render in this build
and the app reports this limitation. The table above is a delivery plan, not a
statement of current support.

## Required validation per API

For every phase, validate fullscreen and borderless windowed mode, resize, seven
blocks, graphs and templates, game capture, mode switching, hide/reopen,
partial/stale frames, and failure without fallback. On Linux, include X11/
XWayland and Wayland where applicable. On Windows, verify only allowlisted
executables load the hook and failures appear in Diagnostics. Anti-cheat tests
must respect existing blocks.
