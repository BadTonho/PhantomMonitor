#pragma once

#include "OverlayFrame.h"

#include <chrono>
#include <cstddef>

namespace phantom::overlay {

class FrameReader {
public:
    FrameReader() = default;
    ~FrameReader();
    FrameReader(const FrameReader&) = delete;
    FrameReader& operator=(const FrameReader&) = delete;

    bool TryRead(Frame& frame, std::string& error);
    void Close();

private:
    bool Open(std::string& error);
    const unsigned char* mapping_ = nullptr;
    std::size_t mapping_size_ = 0;
    void* handle_ = nullptr;
    int descriptor_ = -1;
    std::chrono::steady_clock::time_point next_open_attempt_{};
    std::uint64_t cached_sequence_ = 0;
    Frame cached_frame_;
};

} // namespace phantom::overlay
