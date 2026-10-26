#pragma once

#include <vulkan/vulkan.h>

namespace phantom::vulkan_overlay {

// Restrict augmentation to simple, first-time swapchains where the driver
// explicitly supports the combined usage. Recreating an old swapchain is not
// retried: Vulkan retires oldSwapchain even when creation fails.
inline bool CanAddColorAttachment(const VkSwapchainCreateInfoKHR& info,
                                  const VkSurfaceCapabilitiesKHR& surface,
                                  const VkImageFormatProperties& format) {
    if (info.oldSwapchain != VK_NULL_HANDLE || info.pNext != nullptr || info.flags != 0
        || info.imageArrayLayers != 1 || info.surface == VK_NULL_HANDLE
        || (info.imageUsage & VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT) != 0)
        return false;
    if (info.presentMode != VK_PRESENT_MODE_FIFO_KHR && info.presentMode != VK_PRESENT_MODE_FIFO_RELAXED_KHR
        && info.presentMode != VK_PRESENT_MODE_MAILBOX_KHR && info.presentMode != VK_PRESENT_MODE_IMMEDIATE_KHR)
        return false;
    const auto usage = info.imageUsage | VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    return (surface.supportedUsageFlags & usage) == usage
        && info.imageExtent.width <= format.maxExtent.width
        && info.imageExtent.height <= format.maxExtent.height
        && format.maxArrayLayers >= 1 && (format.sampleCounts & VK_SAMPLE_COUNT_1_BIT) != 0;
}

inline VkResult CreateSwapchainWithFallback(
    PFN_vkCreateSwapchainKHR next, VkDevice device, const VkSwapchainCreateInfoKHR* original,
    const VkSwapchainCreateInfoKHR& adjusted, bool enhanced, const VkAllocationCallbacks* allocator,
    VkSwapchainKHR* swapchain, const VkSwapchainCreateInfoKHR*& applied) {
    enhanced = enhanced && original && original->oldSwapchain == VK_NULL_HANDLE;
    applied = enhanced ? &adjusted : original;
    auto result = next(device, applied, allocator, swapchain);
    if (enhanced && (result == VK_ERROR_FORMAT_NOT_SUPPORTED || result == VK_ERROR_INITIALIZATION_FAILED)) {
        applied = original;
        result = next(device, applied, allocator, swapchain);
    }
    return result;
}

} // namespace phantom::vulkan_overlay
