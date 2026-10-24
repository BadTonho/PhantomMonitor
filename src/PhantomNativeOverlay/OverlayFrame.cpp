#include "OverlayFrame.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <limits>
#include <sstream>
#include <string_view>

namespace phantom::overlay {
namespace {

constexpr std::size_t kMaxBlocks = 128;
constexpr std::size_t kMaxElementsPerBlock = 4096;
constexpr std::size_t kMaxSamplesPerGraph = 1200;

bool parse_number(std::string_view value, float& result) {
    if (value.empty()) return false;
    std::string copy(value);
    char* end = nullptr;
    const double parsed = std::strtod(copy.c_str(), &end);
    if (end == copy.c_str() || *end != '\0' || !std::isfinite(parsed)
        || parsed > std::numeric_limits<float>::max()
        || parsed < -std::numeric_limits<float>::max()) return false;
    result = static_cast<float>(parsed);
    return true;
}

bool parse_flag(std::string_view value, bool& result) {
    if (value == "0") { result = false; return true; }
    if (value == "1") { result = true; return true; }
    return false;
}

bool parse_color(std::string_view value, std::uint32_t& result) {
    if (value.size() != 8) return false;
    char* end = nullptr;
    const std::string copy(value);
    const auto parsed = std::strtoul(copy.c_str(), &end, 16);
    if (end != copy.c_str() + copy.size() || parsed > 0xFFFFFFFFUL) return false;
    result = static_cast<std::uint32_t>(parsed);
    return true;
}

bool decode_base64(std::string_view input, std::string& output, bool allow_empty = true) {
    if (input.empty()) return allow_empty;
    if ((input.size() & 3U) != 0) return false;
    auto digit = [](char value) -> int {
        if (value >= 'A' && value <= 'Z') return value - 'A';
        if (value >= 'a' && value <= 'z') return value - 'a' + 26;
        if (value >= '0' && value <= '9') return value - '0' + 52;
        if (value == '+') return 62;
        if (value == '/') return 63;
        return -1;
    };

    output.clear();
    output.reserve((input.size() / 4) * 3);
    for (std::size_t index = 0; index < input.size(); index += 4) {
        const bool last = index + 4 == input.size();
        const int a = digit(input[index]);
        const int b = digit(input[index + 1]);
        const int c = input[index + 2] == '=' ? 0 : digit(input[index + 2]);
        const int d = input[index + 3] == '=' ? 0 : digit(input[index + 3]);
        const bool pad_c = input[index + 2] == '=';
        const bool pad_d = input[index + 3] == '=';
        if (a < 0 || b < 0 || c < 0 || d < 0 || (!last && (pad_c || pad_d))
            || (pad_c && !pad_d)) return false;
        const std::uint32_t bits = (static_cast<std::uint32_t>(a) << 18)
            | (static_cast<std::uint32_t>(b) << 12)
            | (static_cast<std::uint32_t>(c) << 6)
            | static_cast<std::uint32_t>(d);
        output.push_back(static_cast<char>((bits >> 16) & 0xFF));
        if (!pad_c) output.push_back(static_cast<char>((bits >> 8) & 0xFF));
        if (!pad_d) output.push_back(static_cast<char>(bits & 0xFF));
    }
    return true;
}

std::vector<std::string_view> split(std::string_view value, char separator) {
    std::vector<std::string_view> fields;
    std::size_t start = 0;
    while (true) {
        const auto position = value.find(separator, start);
        fields.push_back(value.substr(start, position == std::string_view::npos
            ? value.size() - start : position - start));
        if (position == std::string_view::npos) break;
        start = position + 1;
    }
    return fields;
}

bool parse_insets(const std::vector<std::string_view>& fields, std::size_t offset, Insets& result) {
    float* values[] = { &result.left, &result.top, &result.right, &result.bottom };
    for (std::size_t index = 0; index < 4; ++index)
        if (!parse_number(fields[offset + index], *values[index]) || *values[index] < 0 || *values[index] > 10000)
            return false;
    return true;
}

bool parse_samples(std::string_view value, std::vector<float>& result) {
    if (value.empty()) return true;
    const auto fields = split(value, ',');
    if (fields.size() > kMaxSamplesPerGraph) return false;
    result.reserve(fields.size());
    for (auto field : fields) {
        float sample = 0;
        if (!parse_number(field, sample)) return false;
        result.push_back(sample);
    }
    return true;
}

bool bounded(float value, float minimum, float maximum) {
    return std::isfinite(value) && value >= minimum && value <= maximum;
}

} // namespace

FrameReadResult ParseFramePayload(const std::string& payload) {
    FrameReadResult result;
    auto fail = [&](const char* message) {
        result.valid = false;
        result.error = message;
        result.frame = {};
        return result;
    };

    if (payload.empty() || payload.size() > 1024 * 1024 || payload.back() != '\n')
        return fail("payload size or terminator is invalid");

    std::vector<std::string_view> lines;
    std::size_t start = 0;
    while (start < payload.size()) {
        const auto end = payload.find('\n', start);
        if (end == std::string::npos) return fail("unterminated record");
        lines.emplace_back(payload.data() + start, end - start);
        start = end + 1;
    }
    if (lines.size() < 2) return fail("missing frame terminator");

    const auto header = split(lines.front(), '\t');
    if (header.size() != 31 || header[0] != "F" || header[1] != "1")
        return fail("unsupported frame header");
    Frame frame;
    if (!parse_flag(header[2], frame.visible) || !parse_flag(header[3], frame.paused)
        || !parse_number(header[4], frame.width) || !parse_number(header[5], frame.reference_width)
        || !parse_number(header[6], frame.reference_height) || !parse_number(header[7], frame.font_size)
        || !parse_number(header[8], frame.opacity) || !parse_color(header[9], frame.background_color)
        || !parse_flag(header[10], frame.border_enabled) || !parse_color(header[11], frame.border_color)
        || !parse_color(header[12], frame.accent_color) || !parse_flag(header[13], frame.compact)
        || !decode_base64(header[14], frame.name_font_family) || !decode_base64(header[15], frame.value_font_family)
        || !parse_number(header[16], frame.name_font_size)
        || !parse_insets(header, 17, frame.outer_margin) || !parse_insets(header, 21, frame.padding)
        || !parse_number(header[25], frame.content_spacing) || !parse_insets(header, 26, frame.row_margin)
        || !parse_number(header[30], frame.frametime_graph_height))
        return fail("invalid frame header fields");

    if (!bounded(frame.width, 1, 10000) || !bounded(frame.reference_width, 1, 32768)
        || !bounded(frame.reference_height, 1, 32768) || !bounded(frame.font_size, 1, 256)
        || !bounded(frame.opacity, 0, 1) || !bounded(frame.name_font_size, 1, 256)
        || !bounded(frame.content_spacing, 0, 1000) || !bounded(frame.frametime_graph_height, 0, 1000))
        return fail("frame values are outside supported bounds");

    Block* current = nullptr;
    for (std::size_t line_index = 1; line_index < lines.size(); ++line_index) {
        const auto fields = split(lines[line_index], '\t');
        if (fields.empty()) return fail("empty record");
        if (fields[0] == "B") {
            if (current != nullptr || fields.size() != 5 || frame.blocks.size() >= kMaxBlocks)
                return fail("invalid block record");
            Block block;
            if (!decode_base64(fields[1], block.id, false) || block.id.size() > 256
                || !parse_number(fields[2], block.x) || !parse_number(fields[3], block.y)
                || !bounded(block.x, -5000, 5000) || !bounded(block.y, -5000, 5000)
                || !decode_base64(fields[4], block.title) || block.title.size() > 4096)
                return fail("invalid block values");
            frame.blocks.push_back(std::move(block));
            current = &frame.blocks.back();
            continue;
        }
        if (fields[0] == "E") {
            if (current == nullptr || fields.size() != 1) return fail("unexpected block terminator");
            current = nullptr;
            continue;
        }
        if (current == nullptr || current->elements.size() >= kMaxElementsPerBlock)
            return fail("element outside block or too many elements");

        Element element;
        if (fields[0] == "N" || fields[0] == "T") {
            if (fields.size() != 4 || !parse_number(fields[1], element.font_size)
                || !bounded(element.font_size, 1, 256) || !parse_color(fields[2], element.color)
                || !decode_base64(fields[3], element.text) || element.text.size() > 65536)
                return fail("invalid text element");
            element.kind = fields[0] == "N" ? Element::Kind::Name : Element::Kind::Text;
        } else if (fields[0] == "G") {
            if (fields.size() != 6 || !parse_number(fields[1], element.graph_height)
                || !bounded(element.graph_height, 1, 1000) || !parse_color(fields[2], element.color))
                return fail("invalid graph element");
            if (!fields[3].empty()) {
                if (!parse_number(fields[3], element.graph_minimum)) return fail("invalid graph minimum");
                element.has_graph_minimum = true;
            }
            if (!fields[4].empty()) {
                if (!parse_number(fields[4], element.graph_maximum)) return fail("invalid graph maximum");
                element.has_graph_maximum = true;
            }
            if (element.has_graph_minimum && element.has_graph_maximum
                && element.graph_maximum <= element.graph_minimum) return fail("invalid graph range");
            if (!parse_samples(fields[5], element.samples)) return fail("invalid graph samples");
            element.kind = Element::Kind::Graph;
        } else {
            return fail("unknown element kind");
        }
        current->elements.push_back(std::move(element));
    }
    if (current != nullptr) return fail("unterminated block");

    result.valid = true;
    result.frame = std::move(frame);
    return result;
}

PixelPosition ToPixelPosition(const Frame& frame, const Block& block,
                              std::uint32_t width, std::uint32_t height,
                              float block_width, float block_height) {
    if (block_width <= 0 && frame.reference_width > 0)
        block_width = frame.width * static_cast<float>(width) / frame.reference_width;
    return {
        block.x * std::max(0.0f, static_cast<float>(width) - block_width),
        block.y * std::max(0.0f, static_cast<float>(height) - block_height)
    };
}

} // namespace phantom::overlay
