#include "OverlayFrame.h"
#include "OverlayImGuiDraw.h"
#include "imgui.h"

#include <algorithm>
#include <cmath>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#else
#include <sys/mman.h>
#endif

namespace {
int failures = 0;
void check(bool condition, const char* name) {
    if (condition) std::cout << "PASS " << name << '\n';
    else { std::cerr << "FAIL " << name << '\n'; ++failures; }
}

std::string valid_payload() {
    return "F\t1\t1\t0\t220\t960\t760\t11.5\t0.85\tFF141A24\t1\tFF2C527A\tFF1E90FF\t0\tQXBwIFNhbnM=\tVGVzdCBNb25v\t9\t8\t8\t8\t8\t8\t8\t8\t8\t2\t0\t2\t0\t2\t36\n"
        "B\tY3B1\t0.25\t0.5\tQ1BV\n"
        "N\t9\tFF8E9AA8\tQ1BV\n"
        "T\t11.5\tFFF2F5FA\tMjclIC4gNDM1MCBNSHo=\n"
        "G\t36\tFF1E90FF\t0\t33.3\t10,15,20\n"
        "E\n";
}

void write_u32(std::vector<unsigned char>& bytes, std::size_t offset, std::uint32_t value) {
    for (unsigned int i = 0; i < 4; ++i)
        bytes[offset + i] = static_cast<unsigned char>(value >> (i * 8));
}

void write_u64(std::vector<unsigned char>& bytes, std::size_t offset, std::uint64_t value) {
    for (unsigned int i = 0; i < 8; ++i)
        bytes[offset + i] = static_cast<unsigned char>(value >> (i * 8));
}

std::vector<unsigned char> snapshot(const std::string& payload, std::int64_t timestamp,
                                    std::uint64_t sequence = 2) {
    std::vector<unsigned char> bytes(32 + payload.size());
    write_u32(bytes, 0, 0x4F534850);
    write_u32(bytes, 4, 1);
    write_u64(bytes, 8, sequence);
    write_u64(bytes, 16, static_cast<std::uint64_t>(timestamp));
    write_u32(bytes, 24, static_cast<std::uint32_t>(payload.size()));
    std::memcpy(bytes.data() + 32, payload.data(), payload.size());
    return bytes;
}
}

int main() {
    using namespace phantom::overlay;
    const auto parsed = ParseFramePayload(valid_payload());
    if (!parsed.valid) std::cerr << "parse error: " << parsed.error << '\n';
    check(parsed.valid && parsed.frame.visible && parsed.frame.blocks.size() == 1,
          "v1 frame and block parse");
    check(parsed.valid && parsed.frame.name_font_family == "App Sans"
          && parsed.frame.value_font_family == "Test Mono",
          "selected font family names are retained in the presentation model");
    check(parsed.valid && parsed.frame.blocks[0].elements.size() == 3
          && parsed.frame.blocks[0].elements[2].samples.size() == 3,
          "text and graph elements parse in order");
    auto v2_payload = valid_payload();
    v2_payload.replace(0, 3, "F\t2");
    v2_payload.insert(v2_payload.rfind("E\n"), "W\t16\tFFF2F5FA\tR1BV\t80\n");
    const auto extended = ParseFramePayload(v2_payload);
    check(extended.valid && extended.frame.blocks[0].elements.back().text == "GPU"
          && extended.frame.blocks[0].elements.back().text_width == 80,
          "v2 custom line retains font, text and width");
    auto invalid_width = v2_payload;
    invalid_width.replace(invalid_width.find("\t80\n"), 4, "\t0\n");
    check(!ParseFramePayload(invalid_width).valid, "v2 rejects zero width");
    auto invalid_legacy = v2_payload;
    invalid_legacy.replace(0, 3, "F\t1");
    check(!ParseFramePayload(invalid_legacy).valid, "v1 rejects v2 custom records");
    if (parsed.valid) {
        const auto at_1080p = ToPixelPosition(parsed.frame, parsed.frame.blocks[0],
            1920, 1080, 440, 100);
        const auto at_1440p = ToPixelPosition(parsed.frame, parsed.frame.blocks[0],
            2560, 1440, 586.6667f, 133.3333f);
        check(std::abs(at_1080p.x - 370.0f) < 0.01f && std::abs(at_1080p.y - 490.0f) < 0.01f,
              "normalized coordinates map at 1080p with block dimensions");
        check(std::abs(at_1440p.x - 493.3333f) < 0.02f && std::abs(at_1440p.y - 653.3333f) < 0.02f,
              "normalized coordinates and dimensions scale at 1440p");
    }

    // Inspect the actual ImGui vertices: wrapped titles and rows must fit in the
    // background and leave room for the next row, without a graphics backend.
    auto* context = ImGui::CreateContext();
    ConfigureOverlayFonts();
    unsigned char* pixels = nullptr;
    int atlas_width = 0, atlas_height = 0;
    ImGui::GetIO().Fonts->GetTexDataAsRGBA32(&pixels, &atlas_width, &atlas_height);
    ImGui::GetIO().Fonts->SetTexID(1);
    Frame layout;
    layout.visible = true;
    layout.width = 120;
    layout.reference_width = 800;
    layout.reference_height = 600;
    layout.font_size = 12;
    layout.name_font_size = 12;
    layout.opacity = 1;
    layout.background_color = 0xFF000000;
    layout.accent_color = 0xFF0000FF;
    layout.border_enabled = false;
    layout.padding = {6, 6, 6, 6};
    layout.content_spacing = 4;
    layout.row_margin = {0, 2, 0, 2};
    Block block;
    block.title = "A title with enough words to wrap over multiple lines";
    Element first;
    first.kind = Element::Kind::Text;
    first.font_size = 12;
    first.color = 0xFFFF0000;
    first.text = "A long metric value which must wrap onto several lines without overlap";
    first.text_width = 55;
    Element second;
    second.kind = Element::Kind::Text;
    second.font_size = 12;
    second.color = 0xFF00FF00;
    second.text = "Next row";
    block.elements = {first, second};
    layout.blocks = {block};
    ImDrawData* drawn = nullptr;
    DrawFrame(layout, 800, 600, 1, 1, &drawn);
    float title_bottom = -1, first_top = 1e6f, first_bottom = -1, second_top = 1e6f, second_bottom = -1, background_bottom = -1;
    if (drawn) for (int i = 0; i < drawn->CmdListsCount; ++i) {
        for (const auto& vertex : drawn->CmdLists[i]->VtxBuffer) {
            if (vertex.col == IM_COL32(0, 0, 255, 255)) title_bottom = std::max(title_bottom, vertex.pos.y);
            if (vertex.col == IM_COL32(255, 0, 0, 255)) { first_top = std::min(first_top, vertex.pos.y); first_bottom = std::max(first_bottom, vertex.pos.y); }
            if (vertex.col == IM_COL32(0, 255, 0, 255)) { second_top = std::min(second_top, vertex.pos.y); second_bottom = std::max(second_bottom, vertex.pos.y); }
            if (vertex.col == IM_COL32(0, 0, 0, 255)) background_bottom = std::max(background_bottom, vertex.pos.y);
        }
    }
    check(pixels && title_bottom >= 0 && first_bottom >= 0 && second_bottom >= 0
          && first_top >= title_bottom && second_top >= first_bottom && background_bottom >= second_bottom,
          "wrapped title and metric rows do not overlap or escape the background");
    ImGui::DestroyContext(context);

    const auto now = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count();
    auto bytes = snapshot(valid_payload(), now);
    Frame snapshot_frame;
    std::uint64_t sequence = 0;
    std::string snapshot_error;
    check(TryParseFrameSnapshot(bytes.data(), bytes.size(), now, snapshot_frame, sequence, snapshot_error)
          && sequence == 2 && snapshot_frame.blocks.size() == 1,
          "complete stable shared-memory snapshot accepted");
#if defined(_WIN32)
    auto* readonly = static_cast<unsigned char*>(VirtualAlloc(nullptr, bytes.size(), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    if (readonly) {
        std::memcpy(readonly, bytes.data(), bytes.size());
        DWORD old = 0;
        const bool protected_page = VirtualProtect(readonly, bytes.size(), PAGE_READONLY, &old) != 0;
        check(protected_page && TryParseFrameSnapshot(readonly, bytes.size(), now, snapshot_frame, sequence, snapshot_error),
              "Windows read-only mapping can be read without a write access violation");
        VirtualFree(readonly, 0, MEM_RELEASE);
    } else check(false, "allocate read-only mapping test");
#endif
    check(!TryParseFrameSnapshot(bytes.data(), bytes.size() - 1, now,
          snapshot_frame, sequence, snapshot_error), "partial snapshot rejected");
    bytes = snapshot(valid_payload(), now, 3);
    check(!TryParseFrameSnapshot(bytes.data(), bytes.size(), now,
          snapshot_frame, sequence, snapshot_error), "in-progress publication rejected");
    bytes = snapshot(valid_payload(), now - 3001);
    check(!TryParseFrameSnapshot(bytes.data(), bytes.size(), now,
          snapshot_frame, sequence, snapshot_error) && snapshot_error == "frame is stale",
          "stale snapshot rejected");
    bytes = snapshot(valid_payload(), now);
    write_u32(bytes, 4, 99);
    check(!TryParseFrameSnapshot(bytes.data(), bytes.size(), now,
          snapshot_frame, sequence, snapshot_error), "unknown shared-memory version rejected");
    bytes = snapshot("F\t1\t1\n", now);
    check(!TryParseFrameSnapshot(bytes.data(), bytes.size(), now,
          snapshot_frame, sequence, snapshot_error), "malformed snapshot payload rejected");
    auto malformed = ParseFramePayload("F\t1\t1\n");
    check(!malformed.valid, "malformed frame rejected");
    malformed = ParseFramePayload(valid_payload().substr(0, valid_payload().size() - 1));
    check(!malformed.valid, "truncated frame rejected");
    malformed = ParseFramePayload("F\t3\t1\t0\t220\t960\t760\t11.5\t0.85\tFF141A24\t1\tFF2C527A\tFF1E90FF\t0\t\t\t9\t8\t8\t8\t8\t8\t8\t8\t8\t2\t0\t2\t0\t2\t36\n\n");
    check(!malformed.valid, "unknown payload version rejected");
    return failures == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
