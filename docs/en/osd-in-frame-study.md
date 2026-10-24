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

## Implemented renderers

| Platform | Native APIs in the code | Activation |
|---|---|---|
| Linux | Vulkan; OpenGL through GLX/EGL (OpenGL ES 3+) | Vulkan layer enabled per game; OpenGL through `LD_PRELOAD` |
| Windows | Direct3D 9–12, OpenGL/WGL, and Vulkan | x86/x64 hooks for allowlisted executables; Vulkan layer enabled when launched by Phantom Monitor |

The renderers use an internal API-independent presentation and Dear ImGui as
the initial drawing implementation. The presentation channel uses sequence
validation, checks protocol version and size, and rejects frames older than
three seconds. Native diagnostics use a separate channel for each renderer and
process so a Vulkan layer and DXGI hook in the same game do not overwrite each
other. Hidden, invalid, or stale frames are not drawn.

Multiple presentations are currently limited: Vulkan requires one swapchain
per present call; the Windows hook follows the first DXGI swapchain in a
process; and the Linux OpenGL preload supports one context per process.
Diagnostics reports these limits. EGL with OpenGL ES requires OpenGL ES 3 or
newer. The v1 frame preserves the selected font family names, but the current
renderer uses Dear ImGui's built-in default font; size, colors, and geometry are
applied, while a font family selected in the editor is not yet loaded by the
native renderer.

On Linux, users can start a configured executable from Phantom Monitor, which
sets `ENABLE_PHANTOM_OVERLAY=1`, or copy that variable into the game's Steam
launch options. Per-process gating also keeps the existing Vulkan FPS layer
available.

## Status and validation

The code includes the Linux Vulkan layer with drawing and the existing
FPS/frametime collection, the OpenGL GLX/EGL renderer, Windows Direct3D 9–12 and
WGL hooks, and a Windows Vulkan layer for games launched by Phantom Monitor.
The Windows hook selects an x86 or x64 launcher from the executable and injects
the DLL only into allowlisted processes; detection of games launched elsewhere
works while the app is open. The **Launch** button keeps the game suspended
until hook initialization finishes. Failures appear in Diagnostics and never
open floating windows automatically.

The Linux build compiles the Vulkan layer, OpenGL preload, and native tests.
Windows components require Visual Studio 2022 and the Vulkan SDK; they must be
built on Windows and need runtime validation on x86/x64. Source presence and a
Linux build do not replace that validation. Anti-cheats may reject the DLL and
there is no attempt to bypass them.

## Required validation per API

For each API, validate fullscreen and borderless windowed mode, resize, seven
blocks, graphs and templates, game capture, mode switching, hide/reopen,
partial/stale frames, and failure without fallback. On Linux, include X11/
XWayland and Wayland when available. On Windows, validate x86 and x64, both
launch paths, the executable allowlist, and the Vulkan layer. Anti-cheat tests
must respect existing blocks.
