#include "OverlayImGuiDraw.h"

#include "imgui.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace phantom::overlay {
namespace {

ImU32 color(std::uint32_t argb, float opacity) {
    const auto a = static_cast<float>((argb >> 24) & 0xFF) / 255.0f * opacity;
    const auto r = static_cast<float>((argb >> 16) & 0xFF) / 255.0f;
    const auto g = static_cast<float>((argb >> 8) & 0xFF) / 255.0f;
    const auto b = static_cast<float>(argb & 0xFF) / 255.0f;
    return ImGui::ColorConvertFloat4ToU32(ImVec4(r, g, b, a));
}

float measure_wrapped(ImFont* font, float size, const std::string& text, float width) {
    if (text.empty()) return size;
    const auto measured = font->CalcTextSizeA(size, width, 0.0f, text.c_str(), text.c_str() + text.size());
    return std::max(size, measured.y);
}

void draw_graph(ImDrawList* draw, const Element& element, ImVec2 origin,
                float width, float height, float opacity) {
    const ImVec2 bottom(origin.x + width, origin.y + height);
    const auto stroke = color(element.color, opacity);
    draw->AddRectFilled(origin, bottom, IM_COL32(16, 20, 29, 130), 2.0f);
    draw->AddLine(ImVec2(origin.x, origin.y + height * 0.5f),
                  ImVec2(bottom.x, origin.y + height * 0.5f), IM_COL32(130, 145, 160, 50));
    if (element.samples.size() < 2) return;

    float minimum = element.has_graph_minimum
        ? element.graph_minimum : *std::min_element(element.samples.begin(), element.samples.end());
    float maximum = element.has_graph_maximum
        ? element.graph_maximum : *std::max_element(element.samples.begin(), element.samples.end());
    if (!element.has_graph_minimum && !element.has_graph_maximum && minimum >= 0.0f)
        maximum = std::max(maximum, 33.3f);
    if (maximum - minimum < 0.0001f) maximum = minimum + 1.0f;

    const auto count = element.samples.size();
    for (std::size_t i = 1; i < count; ++i) {
        const float x0 = origin.x + width * static_cast<float>(i - 1) / static_cast<float>(count - 1);
        const float x1 = origin.x + width * static_cast<float>(i) / static_cast<float>(count - 1);
        const float y0 = bottom.y - height * std::clamp((element.samples[i - 1] - minimum) / (maximum - minimum), 0.0f, 1.0f);
        const float y1 = bottom.y - height * std::clamp((element.samples[i] - minimum) / (maximum - minimum), 0.0f, 1.0f);
        draw->AddLine(ImVec2(x0, y0), ImVec2(x1, y1), stroke, 1.5f);
    }
}

} // namespace

void DrawFrame(const Frame& frame, std::uint32_t framebuffer_width,
               std::uint32_t framebuffer_height, float display_scale_x,
               float display_scale_y, ImDrawData** draw_data) {
    if (draw_data) *draw_data = nullptr;
    if (!frame.visible || framebuffer_width == 0 || framebuffer_height == 0) return;

    ImGuiIO& io = ImGui::GetIO();
    io.DisplaySize = ImVec2(static_cast<float>(framebuffer_width), static_cast<float>(framebuffer_height));
    io.DisplayFramebufferScale = ImVec2(1.0f, 1.0f);
    io.DeltaTime = 1.0f / 60.0f;
    io.IniFilename = nullptr;
    io.LogFilename = nullptr;
    io.ConfigFlags |= ImGuiConfigFlags_NoMouse | ImGuiConfigFlags_NoKeyboard;

    ImGui::NewFrame();
    ImDrawList* draw = ImGui::GetForegroundDrawList();
    const float opacity = std::clamp(frame.opacity, 0.0f, 1.0f);
    const float sx = std::max(0.01f, display_scale_x);
    const float sy = std::max(0.01f, display_scale_y);
    ImFont* font = io.FontDefault != nullptr ? io.FontDefault : io.Fonts->Fonts[0];
    for (const auto& block : frame.blocks) {
        const float width = std::max(1.0f, frame.width * sx
            - frame.outer_margin.left * sx - frame.outer_margin.right * sx);
        const float pad_left = frame.padding.left * sx;
        const float pad_top = frame.padding.top * sy;
        const float pad_right = frame.padding.right * sx;
        const float pad_bottom = frame.padding.bottom * sy;
        const float row_left = frame.row_margin.left * sx;
        const float row_right = frame.row_margin.right * sx;
        const float row_top = frame.row_margin.top * sy;
        const float row_bottom = frame.row_margin.bottom * sy;
        const float inner_width = std::max(1.0f, width - pad_left - pad_right);
        const float text_width = std::max(1.0f, inner_width - row_left - row_right);
        const float title_size = std::max(1.0f, frame.name_font_size * sy);
        const float title_height = font->CalcTextSizeA(title_size, text_width, 0.0f,
            block.title.c_str(), block.title.c_str() + block.title.size()).y;
        float content_height = title_height;
        for (const auto& element : block.elements) {
            if (element.kind == Element::Kind::Graph) {
                content_height += element.graph_height * sy + row_top + row_bottom;
                continue;
            }
            const float text_size = std::max(1.0f, (element.font_size > 0 ? element.font_size : frame.font_size) * sy);
            content_height += measure_wrapped(font, text_size, element.text, text_width) + row_top + row_bottom;
            if (element.kind == Element::Kind::Name) content_height += 2.0f * sy;
        }
        const float height = content_height + pad_top + pad_bottom + frame.content_spacing * sy;
        const auto position = ToPixelPosition(frame, block, framebuffer_width, framebuffer_height,
            frame.width * sx, height + frame.outer_margin.top * sy + frame.outer_margin.bottom * sy);
        const float x = position.x + frame.outer_margin.left * sx;
        const float y = position.y + frame.outer_margin.top * sy;
        const ImVec2 content_start(x + pad_left, y + pad_top);
        const ImVec2 upper(x, y);
        const ImVec2 lower(x + width, y + height);
        draw->AddRectFilled(upper, lower, color(frame.background_color, opacity), 6.0f * sy);
        if (frame.border_enabled)
            draw->AddRect(upper, lower, color(frame.border_color, opacity), 6.0f * sy, 0, std::max(1.0f, sx));

        float cursor_y = content_start.y;
        draw->AddText(font, title_size, ImVec2(content_start.x, cursor_y),
            color(frame.accent_color, opacity), block.title.c_str(), block.title.c_str() + block.title.size(), text_width);
        cursor_y += title_height + frame.content_spacing * sy;
        for (const auto& element : block.elements) {
            cursor_y += row_top;
            if (element.kind == Element::Kind::Graph) {
                const float graph_height = element.graph_height * sy;
                draw_graph(draw, element, ImVec2(content_start.x + row_left, cursor_y), text_width,
                    graph_height, opacity);
                cursor_y += graph_height;
            } else {
                const float text_size = std::max(1.0f, (element.font_size > 0 ? element.font_size : frame.font_size) * sy);
                draw->AddText(font, text_size,
                    ImVec2(content_start.x + row_left, cursor_y), color(element.color, opacity),
                    element.text.c_str(), element.text.c_str() + element.text.size(), text_width);
                cursor_y += measure_wrapped(font, text_size, element.text, text_width);
                if (element.kind == Element::Kind::Name) cursor_y += 2.0f * sy;
            }
            cursor_y += row_bottom;
        }
    }

    ImGui::Render();
    if (draw_data) *draw_data = ImGui::GetDrawData();
}

} // namespace phantom::overlay
