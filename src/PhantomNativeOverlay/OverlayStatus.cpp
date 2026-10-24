#include "OverlayStatus.h"

#include <algorithm>
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

constexpr std::uint32_t kMagic = 0x53504850; // "PHPS"
constexpr std::uint32_t kVersion = 1;

#pragma pack(push, 1)
struct SharedStatus {
    std::uint32_t magic;
    std::uint32_t version;
    std::uint64_t sequence;
    std::uint32_t pid;
    std::uint32_t api;
    std::uint32_t state;
    std::int64_t updated_unix_ms;
    char message[256];
};
#pragma pack(pop)

std::uint32_t current_pid() {
#if defined(_WIN32)
    return GetCurrentProcessId();
#else
    return static_cast<std::uint32_t>(getpid());
#endif
}

} // namespace

StatusPublisher::StatusPublisher(StatusProducer producer) {
    switch (producer) {
    case StatusProducer::Vulkan: producer_name_ = "vulkan"; break;
    case StatusProducer::OpenGL: producer_name_ = "opengl"; break;
    case StatusProducer::WindowsHook: producer_name_ = "windows"; break;
    }
#if defined(_WIN32)
    name_ = "Local\\PhantomMonitor.InGameOverlay.Status.v1." + std::to_string(current_pid())
        + "." + producer_name_;
    HANDLE mapping = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
        0, sizeof(SharedStatus), name_.c_str());
    if (mapping == nullptr) return;
    void* view = MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(SharedStatus));
    if (view == nullptr) { CloseHandle(mapping); return; }
    handle_ = mapping;
    mapping_ = view;
#elif defined(__linux__)
    name_ = "/phantommonitor_overlay_status_v1_" + std::to_string(static_cast<unsigned long long>(getuid()))
        + "_" + std::to_string(current_pid()) + "_" + producer_name_;
    descriptor_ = shm_open(name_.c_str(), O_CREAT | O_RDWR | O_CLOEXEC, 0600);
    if (descriptor_ < 0 || ftruncate(descriptor_, sizeof(SharedStatus)) != 0) {
        if (descriptor_ >= 0) close(descriptor_);
        descriptor_ = -1;
        return;
    }
    if (fchmod(descriptor_, 0600) != 0) {
        close(descriptor_);
        descriptor_ = -1;
        return;
    }
    void* view = mmap(nullptr, sizeof(SharedStatus), PROT_READ | PROT_WRITE, MAP_SHARED, descriptor_, 0);
    if (view == MAP_FAILED) {
        close(descriptor_);
        descriptor_ = -1;
        return;
    }
    mapping_ = view;
#endif
    if (mapping_ != nullptr) {
        std::memset(mapping_, 0, sizeof(SharedStatus));
        auto* status = static_cast<SharedStatus*>(mapping_);
        status->magic = kMagic;
        status->version = kVersion;
        status->pid = current_pid();
    }
}

void StatusPublisher::Update(RenderApi api, RenderState state, const std::string& message) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (mapping_ == nullptr) return;
    const auto api_value = static_cast<std::uint32_t>(api);
    const auto state_value = static_cast<std::uint32_t>(state);
    const auto updated_unix_ms = static_cast<std::int64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::system_clock::now().time_since_epoch()).count());
    if (api_ == api_value && state_ == state_value && message_ == message
        && updated_unix_ms >= last_updated_unix_ms_
        && updated_unix_ms - last_updated_unix_ms_ < 10'000) return;
    api_ = api_value;
    state_ = state_value;
    message_ = message;

    auto* status = static_cast<SharedStatus*>(mapping_);
    auto odd = sequence_ + 1;
    if ((odd & 1U) == 0) ++odd;
#if defined(_WIN32)
    InterlockedExchange64(reinterpret_cast<volatile LONG64*>(&status->sequence), static_cast<LONG64>(odd));
#else
    __atomic_store_n(&status->sequence, odd, __ATOMIC_RELEASE);
#endif
    status->pid = current_pid();
    status->api = api_value;
    status->state = state_value;
    status->updated_unix_ms = updated_unix_ms;
    std::memset(status->message, 0, sizeof(status->message));
    std::memcpy(status->message, message.data(), std::min(message.size(), sizeof(status->message) - 1));
#if defined(_WIN32)
    MemoryBarrier();
    InterlockedExchange64(reinterpret_cast<volatile LONG64*>(&status->sequence), static_cast<LONG64>(odd + 1));
#else
    __atomic_store_n(&status->sequence, odd + 1, __ATOMIC_RELEASE);
#endif
    sequence_ = odd + 1;
    last_updated_unix_ms_ = updated_unix_ms;
}

StatusPublisher::~StatusPublisher() {
    if (mapping_ != nullptr) {
#if defined(_WIN32)
        UnmapViewOfFile(mapping_);
#else
        munmap(mapping_, sizeof(SharedStatus));
#endif
        mapping_ = nullptr;
    }
#if defined(_WIN32)
    if (handle_ != nullptr) CloseHandle(static_cast<HANDLE>(handle_));
#else
    if (descriptor_ >= 0) close(descriptor_);
    if (!name_.empty()) shm_unlink(name_.c_str());
#endif
}

} // namespace phantom::overlay
