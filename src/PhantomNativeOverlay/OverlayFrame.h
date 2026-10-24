#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace phantom::overlay {

struct Insets {
    float left = 0;
    float top = 0;
    float right = 0;
    float bottom = 0;
};

struct Element {
    enum class Kind { Name, Text, Graph } kind = Kind::Text;
    float font_size = 0;
    std::uint32_t color = 0xFFFFFFFF;
    std::string text;
    float graph_height = 0;
    float graph_minimum = 0;
    float graph_maximum = 0;
    bool has_graph_minimum = false;
    bool has_graph_maximum = false;
    std::vector<float> samples;
};

struct Block {
    std::string id;
    float x = 0;
    float y = 0;
    std::string title;
    std::vector<Element> elements;
};

struct Frame {
    bool visible = false;
    bool paused = false;
    float width = 0;
    float reference_width = 0;
    float reference_height = 0;
    float font_size = 0;
    float opacity = 1;
    std::uint32_t background_color = 0xFF141A24;
    bool border_enabled = true;
    std::uint32_t border_color = 0xFF2C527A;
    std::uint32_t accent_color = 0xFF1E90FF;
    bool compact = false;
    std::string name_font_family;
    std::string value_font_family;
    float name_font_size = 0;
    Insets outer_margin;
    Insets padding;
    float content_spacing = 0;
    Insets row_margin;
    float frametime_graph_height = 0;
    std::vector<Block> blocks;
};

struct FrameReadResult {
    bool valid = false;
    Frame frame;
    std::string error;
};

// Parses the internal v1 payload published by PhantomMonitor. Unknown or
// malformed records are rejected as a whole so renderers never draw partial UI.
FrameReadResult ParseFramePayload(const std::string& payload);

// Maps normalized preview positions to the active swapchain dimensions.
struct PixelPosition { float x; float y; };
PixelPosition ToPixelPosition(const Frame& frame, const Block& block,
                              std::uint32_t width, std::uint32_t height,
                              float block_width = 0, float block_height = 0);

// Validates and copies one complete shared-memory publication. Kept separate
// from OS mapping so the protocol's partial/stale-frame rules can be tested.
bool TryParseFrameSnapshot(const unsigned char* bytes, std::size_t size,
                           std::int64_t now_unix_ms, Frame& frame,
                           std::uint64_t& sequence, std::string& error);

} // namespace phantom::overlay
