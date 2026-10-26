#pragma once

#include "OverlayFrame.h"

struct ImDrawData;

namespace phantom::overlay {

// Configure once for each newly-created ImGui context, before backend initialization.
void ConfigureOverlayFonts();

// Builds API-neutral ImGui draw data. Renderer backends only submit this draw list.
void DrawFrame(const Frame& frame, std::uint32_t framebuffer_width,
               std::uint32_t framebuffer_height, float display_scale_x,
               float display_scale_y, ImDrawData** draw_data);

} // namespace phantom::overlay
