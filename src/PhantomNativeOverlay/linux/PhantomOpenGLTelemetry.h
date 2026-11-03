#pragma once

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <string>

#include <fcntl.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <time.h>
#include <unistd.h>

namespace phantom::overlay::linux_fps {

constexpr std::uint32_t TelemetryMagic = 0x50484E54;
constexpr std::uint32_t TelemetryVersion = 2;
constexpr std::uint32_t FrameCapacity = 4096;
constexpr std::size_t TelemetrySize = 120 + FrameCapacity * 24;

#pragma pack(push, 1)
struct TelemetryHeader {
    std::uint32_t magic;
    std::uint32_t version;
    std::uint32_t pid;
    std::uint64_t frame_count;
    std::uint64_t timestamp_ns;
    float frametime_ms;
    float instant_fps;
    char process_name[64];
};

struct Frame {
    std::uint64_t number;
    std::uint64_t timestamp_ns;
    float frametime_ms;
    float fps;
};

struct Telemetry {
    TelemetryHeader header;
    std::uint32_t capacity;
    std::uint64_t sequence;
    std::uint64_t session;
    Frame frames[FrameCapacity];
};
#pragma pack(pop)

static_assert(sizeof(TelemetryHeader) == 100);
static_assert(offsetof(Telemetry, sequence) == 104);
static_assert(offsetof(Telemetry, frames) == 120);
static_assert(sizeof(Telemetry) == TelemetrySize);

class Publisher {
public:
    Publisher() = default;
    Publisher(const Publisher&) = delete;
    Publisher& operator=(const Publisher&) = delete;

    ~Publisher() { Close(); }

    void RecordPresent() noexcept {
        if (!CaptureEnabled()) return;

        try {
            std::lock_guard<std::mutex> lock(mutex_);
            const auto now = MonotonicNanoseconds();
            if (now == 0) return;
            if (!telemetry_ && !Open()) return;

            if (last_timestamp_ns_ != 0 && now > last_timestamp_ns_) {
                const auto delta = now - last_timestamp_ns_;
                const float frametime_ms = static_cast<float>(delta) / 1'000'000.0f;
                if (frametime_ms >= 0.01f && frametime_ms <= 2000.0f) {
                    const float fps = 1000.0f / frametime_ms;
                    const std::uint64_t frame = ++frame_count_;
                    __atomic_store_n(&telemetry_->sequence, frame * 2 - 1, __ATOMIC_SEQ_CST);
                    telemetry_->frames[(frame - 1) % FrameCapacity] = {frame, now, frametime_ms, fps};
                    telemetry_->header.frame_count = frame;
                    telemetry_->header.timestamp_ns = now;
                    telemetry_->header.frametime_ms = frametime_ms;
                    telemetry_->header.instant_fps = fps;
                    __atomic_store_n(&telemetry_->sequence, frame * 2, __ATOMIC_RELEASE);
                }
            }
            last_timestamp_ns_ = now;
        } catch (...) {
            // Telemetry must never change or interrupt the game's present path.
        }
    }

private:
    static bool CaptureEnabled() noexcept {
        static const bool enabled = [] {
            const auto* value = std::getenv("PHANTOM_FPS_CAPTURE");
            return value != nullptr && std::strcmp(value, "1") == 0;
        }();
        return enabled;
    }

    static std::uint64_t MonotonicNanoseconds() noexcept {
        timespec time{};
        if (clock_gettime(CLOCK_MONOTONIC, &time) != 0) return 0;
        return static_cast<std::uint64_t>(time.tv_sec) * 1'000'000'000ULL
            + static_cast<std::uint64_t>(time.tv_nsec);
    }

    bool Open() noexcept {
        char shm_name[80]{};
        char fallback_name[96]{};
        const auto pid = static_cast<std::uint32_t>(getpid());
        std::snprintf(shm_name, sizeof(shm_name), "/phantom_fps_gl_%u", pid);
        std::snprintf(fallback_name, sizeof(fallback_name), "/tmp/phantom_fps_gl_%u.bin", pid);

        file_descriptor_ = shm_open(shm_name, O_CREAT | O_RDWR | O_CLOEXEC, 0600);
        if (file_descriptor_ >= 0) shm_path_ = shm_name;
        else {
            file_descriptor_ = open(fallback_name, O_CREAT | O_RDWR | O_CLOEXEC, 0600);
            if (file_descriptor_ >= 0) fallback_path_ = fallback_name;
        }
        if (file_descriptor_ < 0 || ftruncate(file_descriptor_, TelemetrySize) != 0) {
            Close();
            return false;
        }

        void* address = mmap(nullptr, TelemetrySize, PROT_READ | PROT_WRITE, MAP_SHARED, file_descriptor_, 0);
        if (address == MAP_FAILED) {
            telemetry_ = nullptr;
            Close();
            return false;
        }

        telemetry_ = static_cast<Telemetry*>(address);
        std::memset(telemetry_, 0, TelemetrySize);
        telemetry_->capacity = FrameCapacity;
        telemetry_->session = MonotonicNanoseconds();
        telemetry_->header.magic = TelemetryMagic;
        telemetry_->header.version = TelemetryVersion;
        telemetry_->header.pid = pid;
        ReadProcessName(telemetry_->header.process_name, sizeof(telemetry_->header.process_name));
        return true;
    }

    static void ReadProcessName(char* destination, std::size_t length) noexcept {
        if (length == 0) return;
        const int fd = open("/proc/self/comm", O_RDONLY | O_CLOEXEC);
        if (fd < 0) return;
        const auto bytes = read(fd, destination, length - 1);
        close(fd);
        if (bytes <= 0) return;
        destination[bytes] = '\0';
        if (bytes > 0 && destination[bytes - 1] == '\n') destination[bytes - 1] = '\0';
    }

    void Close() noexcept {
        if (telemetry_) {
            munmap(telemetry_, TelemetrySize);
            telemetry_ = nullptr;
        }
        if (file_descriptor_ >= 0) {
            close(file_descriptor_);
            file_descriptor_ = -1;
        }
        if (!shm_path_.empty()) {
            shm_unlink(shm_path_.c_str());
            shm_path_.clear();
        }
        if (!fallback_path_.empty()) {
            unlink(fallback_path_.c_str());
            fallback_path_.clear();
        }
    }

    std::mutex mutex_;
    int file_descriptor_ = -1;
    Telemetry* telemetry_ = nullptr;
    std::string shm_path_;
    std::string fallback_path_;
    std::uint64_t last_timestamp_ns_ = 0;
    std::uint64_t frame_count_ = 0;
};

} // namespace phantom::overlay::linux_fps
