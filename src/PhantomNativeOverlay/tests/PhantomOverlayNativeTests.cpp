#include "OverlayFrame.h"

#include <cmath>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>

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

    const auto now = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count();
    auto bytes = snapshot(valid_payload(), now);
    Frame snapshot_frame;
    std::uint64_t sequence = 0;
    std::string snapshot_error;
    check(TryParseFrameSnapshot(bytes.data(), bytes.size(), now, snapshot_frame, sequence, snapshot_error)
          && sequence == 2 && snapshot_frame.blocks.size() == 1,
          "complete stable shared-memory snapshot accepted");
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
    malformed = ParseFramePayload("F\t2\t1\t0\t220\t960\t760\t11.5\t0.85\tFF141A24\t1\tFF2C527A\tFF1E90FF\t0\t\t\t9\t8\t8\t8\t8\t8\t8\t8\t8\t2\t0\t2\t0\t2\t36\n\n");
    check(!malformed.valid, "unknown payload version rejected");
    return failures == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
