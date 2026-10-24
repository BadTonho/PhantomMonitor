#pragma once

#include <vulkan/vulkan.h>
#include <vulkan/vk_layer.h>
#include <cstdint>

namespace phantom::vulkan_overlay {

void RegisterDevice(VkInstance instance, VkPhysicalDevice physical_device, VkDevice device,
                    const VkDeviceCreateInfo* create_info,
                    PFN_vkGetInstanceProcAddr next_gipa,
                    PFN_vkGetDeviceProcAddr next_gdpa);
void RegisterQueue(VkDevice device, VkQueue queue, std::uint32_t queue_family);
void RegisterSwapchain(VkDevice device, VkSwapchainKHR swapchain,
                       const VkSwapchainCreateInfoKHR* create_info,
                       const VkAllocationCallbacks* allocator);
void DestroySwapchain(VkDevice device, VkSwapchainKHR swapchain,
                      const VkAllocationCallbacks* allocator);
void DestroyDevice(VkDevice device);
bool TryPresent(VkDevice device, VkQueue queue, const VkPresentInfoKHR* present_info,
                PFN_vkQueuePresentKHR next_present, VkResult* result);

} // namespace phantom::vulkan_overlay
