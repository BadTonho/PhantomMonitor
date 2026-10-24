#pragma once

#include <cstdint>
#include <mutex>
#include <string>

namespace phantom::overlay {

enum class RenderApi : std::uint32_t { Unknown = 0, Vulkan = 1, Direct3D11 = 2, Direct3D12 = 3, OpenGL = 4, Direct3D9 = 5, Direct3D10 = 6 };
enum class RenderState : std::uint32_t { Waiting = 0, Active = 1, Hidden = 2, Stale = 3, Unsupported = 4, Failed = 5 };
enum class StatusProducer { Vulkan, OpenGL, WindowsHook };

class StatusPublisher {
public:
    explicit StatusPublisher(StatusProducer producer);
    ~StatusPublisher();
    StatusPublisher(const StatusPublisher&) = delete;
    StatusPublisher& operator=(const StatusPublisher&) = delete;
    void Update(RenderApi api, RenderState state, const std::string& message);

private:
    void* mapping_ = nullptr;
    void* handle_ = nullptr;
    int descriptor_ = -1;
    std::string name_;
    std::string producer_name_;
    std::uint64_t sequence_ = 0;
    std::int64_t last_updated_unix_ms_ = 0;
    std::mutex mutex_;
    std::uint32_t api_ = 0;
    std::uint32_t state_ = 0;
    std::string message_;
};

} // namespace phantom::overlay
