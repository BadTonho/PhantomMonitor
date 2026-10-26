# In-game OSD — architecture and phases

> **Language:** English · [Português (Brasil)](../osd-in-frame-estudo.md)

## Decision

Phantom Monitor offers two global modes: **Floating window**, which keeps
blocks in separate windows, and **In-game**, which draws the applied
presentation into the game's frame. Floating remains the default for new and
existing configurations. In-game is opt-in; when its renderer is unavailable or
fails, Diagnostics reports the reason and the app does not switch to windows.

On Windows, **In-game** detects applications presenting frames through one ETW/PresentMon session and attempts the existing x86/x64 Direct3D/OpenGL hook without an executable list. Discovery remains active while the mode is selected, even when sensor collection is stopped. Phantom Monitor, shell, and system processes are excluded; the app never injects PIDs that PresentMon did not observe. ETW may require membership in Performance Log Users. Elevated processes may prevent executable-path lookup, and elevated Vulkan applications may ignore HKCU layer registrations. EAC, BattlEye, Vanguard, and other anti-cheats may reject or block hooks; Phantom Monitor does not bypass them.

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
samples; `E` ends a block.
When a custom line is visible, payload version 2 adds a `W` record with font size, color, Base64 text, and width. Updated renderers accept both v1 and v2 payloads; processes using an older renderer must be restarted after the update.

The shared-memory header
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
| Windows | Direct3D 9–12, OpenGL/WGL, and Vulkan | x86/x64 hooks for PIDs PresentMon identifies as presenting frames; Vulkan implicit layers registered in the matching x86/x64 HKCU registry views while the mode is active |

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

The code includes the Linux Vulkan layer with drawing and existing FPS/frametime collection, the OpenGL GLX/EGL renderer, Windows Direct3D 9–12 and WGL hooks, and a Windows Vulkan layer. On Windows, the PresentMon session stays active when sensor collection stops while **In-game** is selected; the parser records valid PIDs before the foreground-window filter used by FPS metrics. The app resolves each image and architecture, excludes Phantom Monitor and shell/system processes, and tries the matching x86/x64 native launcher once per process, reporting failures without falling back to unobserved PIDs. Vulkan uses implicit manifests in the matching HKCU registry views; registrations are removed when switching to Floating, on app exit, and when starting in Floating mode. The manifest provides `PHANTOMMONITOR_DISABLE_VULKAN_LAYER=1`; Vulkan applications started before registration must restart. Failures appear in Diagnostics and never open floating windows automatically.

The Linux build compiles the Vulkan layer, OpenGL preload, and native tests.
Windows components require Visual Studio 2022 and a Vulkan SDK with loader
import libraries for both x86 and x64; they must be built on Windows and need
runtime validation on x86/x64. With an x64-only SDK, the x86 Direct3D/OpenGL
hooks still build, but the x86 Vulkan layer cannot link. Source presence and a
Linux build do not replace that validation. Anti-cheats may reject the DLL and
there is no attempt to bypass them.

## Required validation per API

Validation still needs fullscreen and borderless modes, resize, seven blocks, graphs and templates, game capture, mode switching, hide/reopen, partial/stale frames, and failure without fallback. On Linux, include X11/XWayland and Wayland when available. On Windows, validate x86 and x64 Direct3D, OpenGL, and Vulkan without registering executables; verify multiple PIDs, expiration without frames, invalid ETW rows, discovery while collection is stopped, and activation/removal in both registry views. Anti-cheat tests must respect existing blocks.
