#pragma once
#include <vulkan/vk_layer.h>
#include <cstdint>

namespace phantom::vulkan_overlay {
inline bool InitializeCommandDispatch(VkDevice device, const VkCommandBuffer* commands,
                                      std::uint32_t count, PFN_vkSetDeviceLoaderData callback) {
    for (std::uint32_t index = 0; index < count; ++index) {
        if (callback) {
            if (callback(device, commands[index]) != VK_SUCCESS) return false;
        } else {
            *reinterpret_cast<void**>(commands[index]) = *reinterpret_cast<void**>(device);
        }
    }
    return true;
}
}
