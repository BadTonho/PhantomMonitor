#include "OverlayFrameReader.h"

#include <chrono>
#include <cstring>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#else
#include <cerrno>
#include <fcntl.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <unistd.h>
#endif

namespace phantom::overlay {
namespace {

constexpr std::size_t kCapacity = 1024 * 1024;
constexpr std::size_t kHeaderSize = 32;
constexpr std::uint32_t kMagic = 0x4F534850;
constexpr std::uint32_t kVersion = 1;
constexpr std::int64_t kMaximumAgeMs = 3000;

std::uint32_t read_u32(const unsigned char* bytes) {
    return static_cast<std::uint32_t>(bytes[0])
        | (static_cast<std::uint32_t>(bytes[1]) << 8)
        | (static_cast<std::uint32_t>(bytes[2]) << 16)
        | (static_cast<std::uint32_t>(bytes[3]) << 24);
}

std::uint64_t read_u64(const unsigned char* bytes) {
    std::uint64_t value = 0;
    for (unsigned int i = 0; i < 8; ++i) value |= static_cast<std::uint64_t>(bytes[i]) << (i * 8);
    return value;
}

std::uint64_t atomic_sequence(const unsigned char* address) {
#if defined(_WIN32)
    return static_cast<std::uint64_t>(InterlockedCompareExchange64(
        reinterpret_cast<volatile LONG64*>(const_cast<unsigned char*>(address)), 0, 0));
#else
    return __atomic_load_n(reinterpret_cast<const std::uint64_t*>(address), __ATOMIC_ACQUIRE);
#endif
}

std::int64_t now_unix_ms() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count();
}

} // namespace

FrameReader::~FrameReader() { Close(); }

bool FrameReader::Open(std::string& error) {
    if (mapping_ != nullptr) return true;
    const auto now = std::chrono::steady_clock::now();
    if (now < next_open_attempt_) {
        error = "shared-memory channel is not available";
        return false;
    }
    next_open_attempt_ = now + std::chrono::seconds(1);
#if defined(_WIN32)
    HANDLE mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, L"Local\\PhantomMonitor.InGameOverlay.v1");
    if (mapping == nullptr) {
        error = "OpenFileMapping failed: " + std::to_string(GetLastError());
        return false;
    }
    void* view = MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, kCapacity);
    if (view == nullptr) {
        error = "MapViewOfFile failed: " + std::to_string(GetLastError());
        CloseHandle(mapping);
        return false;
    }
    handle_ = mapping;
    mapping_ = static_cast<const unsigned char*>(view);
    mapping_size_ = kCapacity;
#elif defined(__linux__)
    const auto name = "/phantommonitor_overlay_v1_" + std::to_string(static_cast<unsigned long long>(getuid()));
    descriptor_ = shm_open(name.c_str(), O_RDONLY | O_CLOEXEC, 0);
    if (descriptor_ < 0) {
        error = "shm_open failed: " + std::to_string(errno);
        return false;
    }
    struct stat info{};
    if (fstat(descriptor_, &info) != 0 || info.st_size < static_cast<off_t>(kCapacity)) {
        error = "shared-memory region is smaller than the protocol capacity";
        Close();
        return false;
    }
    void* view = mmap(nullptr, kCapacity, PROT_READ, MAP_SHARED, descriptor_, 0);
    if (view == MAP_FAILED) {
        error = "mmap failed: " + std::to_string(errno);
        Close();
        return false;
    }
    mapping_ = static_cast<const unsigned char*>(view);
    mapping_size_ = kCapacity;
#else
    error = "shared-memory frame reader is unsupported on this platform";
    return false;
#endif
    return true;
}

bool FrameReader::TryRead(Frame& frame, std::string& error) {
    if (!Open(error)) return false;
    if (mapping_size_ < kHeaderSize || read_u32(mapping_) != kMagic || read_u32(mapping_ + 4) != kVersion) {
        error = "frame protocol magic/version is unsupported";
        return false;
    }

    const auto sequence_before = atomic_sequence(mapping_ + 8);
    if (sequence_before == 0 || (sequence_before & 1U) != 0) {
        error = "frame publication is incomplete";
        return false;
    }
    if (sequence_before == cached_sequence_) {
        const auto cached_age = now_unix_ms() - static_cast<std::int64_t>(read_u64(mapping_ + 16));
        const auto sequence_after = atomic_sequence(mapping_ + 8);
        if (sequence_before == sequence_after && (sequence_after & 1U) == 0
            && cached_age >= -1000 && cached_age <= kMaximumAgeMs) {
            frame = cached_frame_;
            error.clear();
            return true;
        }
    }
    std::uint64_t sequence = 0;
    Frame parsed_frame;
    if (!TryParseFrameSnapshot(mapping_, mapping_size_, now_unix_ms(), parsed_frame, sequence, error))
        return false;
    frame = std::move(parsed_frame);
    cached_frame_ = frame;
    cached_sequence_ = sequence;
    error.clear();
    return true;
}

bool TryParseFrameSnapshot(const unsigned char* bytes, std::size_t size,
                           std::int64_t now, Frame& frame,
                           std::uint64_t& sequence, std::string& error) {
    sequence = 0;
    if (bytes == nullptr || size < kHeaderSize || read_u32(bytes) != kMagic
        || read_u32(bytes + 4) != kVersion) {
        error = "frame protocol magic/version is unsupported";
        return false;
    }

    const auto before = atomic_sequence(bytes + 8);
    if (before == 0 || (before & 1U) != 0) {
        error = "frame publication is incomplete";
        return false;
    }
    const auto timestamp = static_cast<std::int64_t>(read_u64(bytes + 16));
    const auto length = static_cast<std::int32_t>(read_u32(bytes + 24));
    if (length <= 0 || static_cast<std::size_t>(length) > size - kHeaderSize) {
        error = "frame length is invalid";
        return false;
    }
    std::string payload(reinterpret_cast<const char*>(bytes + kHeaderSize),
                        static_cast<std::size_t>(length));
#if defined(_WIN32)
    MemoryBarrier();
#else
    __atomic_thread_fence(__ATOMIC_ACQUIRE);
#endif
    const auto after = atomic_sequence(bytes + 8);
    if (before != after || (after & 1U) != 0) {
        error = "frame changed while it was being read";
        return false;
    }
    const auto age = now - timestamp;
    if (timestamp <= 0 || age < -1000 || age > kMaximumAgeMs) {
        error = "frame is stale";
        return false;
    }
    auto parsed = ParseFramePayload(payload);
    if (!parsed.valid) {
        error = parsed.error;
        return false;
    }
    frame = std::move(parsed.frame);
    sequence = after;
    error.clear();
    return true;
}

void FrameReader::Close() {
    if (mapping_ != nullptr) {
#if defined(_WIN32)
        UnmapViewOfFile(mapping_);
#else
        munmap(const_cast<unsigned char*>(mapping_), mapping_size_);
#endif
        mapping_ = nullptr;
        mapping_size_ = 0;
    }
#if defined(_WIN32)
    if (handle_ != nullptr) {
        CloseHandle(static_cast<HANDLE>(handle_));
        handle_ = nullptr;
    }
#else
    if (descriptor_ >= 0) {
        close(descriptor_);
        descriptor_ = -1;
    }
#endif
    cached_sequence_ = 0;
    cached_frame_ = {};
    next_open_attempt_ = {};
}

} // namespace phantom::overlay
