#include <vulkan/vulkan.h>
#include <vulkan/vk_layer.h>
#include <cstring>
#include <iostream>
#include "../PhantomVulkanDispatch.h"
#include "../PhantomSwapchainPolicy.h"
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#endif

extern "C" PFN_vkVoidFunction VKAPI_CALL Phantom_vkGetInstanceProcAddr(VkInstance, const char*);
extern "C" PFN_vkVoidFunction VKAPI_CALL Phantom_vkGetDeviceProcAddr(VkDevice, const char*);
extern "C" VkResult VKAPI_CALL Phantom_vkNegotiateLoaderLayerInterfaceVersion(VkNegotiateLayerInterface*);

namespace {
struct Handle { void* dispatch; } instance{reinterpret_cast<void*>(0x1234)};
bool queue2_available = true;
unsigned int callback_count = 0;
unsigned int swapchain_calls = 0;
VkImageUsageFlags first_usage = 0, second_usage = 0;
VKAPI_ATTR VkResult VKAPI_CALL create_test_swapchain(VkDevice, const VkSwapchainCreateInfoKHR* info,
                                                      const VkAllocationCallbacks*, VkSwapchainKHR*) {
    ++swapchain_calls;
    if (swapchain_calls == 1) { first_usage = info->imageUsage; return VK_ERROR_FORMAT_NOT_SUPPORTED; }
    second_usage = info->imageUsage;
    return VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL set_loader_data(VkDevice device, void* object) {
    ++callback_count;
    *reinterpret_cast<void**>(object) = *reinterpret_cast<void**>(device);
    return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL stub() {}
VKAPI_ATTR void VKAPI_CALL destroy(VkInstance, const VkAllocationCallbacks*) {}
VKAPI_ATTR VkResult VKAPI_CALL create(const VkInstanceCreateInfo*, const VkAllocationCallbacks*, VkInstance* result) {
    *result = reinterpret_cast<VkInstance>(&instance); return VK_SUCCESS;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL next_gipa(VkInstance, const char* name) {
    if (!queue2_available && std::strcmp(name, "vkGetDeviceQueue2") == 0) return nullptr;
    if (std::strcmp(name, "vkCreateInstance") == 0) return reinterpret_cast<PFN_vkVoidFunction>(create);
    if (std::strcmp(name, "vkDestroyInstance") == 0) return reinterpret_cast<PFN_vkVoidFunction>(destroy);
    return stub;
}
}

int main() {
    VkSwapchainCreateInfoKHR swapchain{};
    swapchain.surface = (VkSurfaceKHR)(uintptr_t)1;
    swapchain.imageUsage = VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    swapchain.imageArrayLayers = 1;
    swapchain.imageExtent = {1920, 1080};
    swapchain.presentMode = VK_PRESENT_MODE_FIFO_KHR;
    VkSurfaceCapabilitiesKHR surface{};
    surface.supportedUsageFlags = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    VkImageFormatProperties format{};
    format.maxExtent = {4096, 4096, 1};
    format.maxArrayLayers = 1;
    format.sampleCounts = VK_SAMPLE_COUNT_1_BIT;
    using phantom::vulkan_overlay::CanAddColorAttachment;
    if (!CanAddColorAttachment(swapchain, surface, format)) return 8;
    surface.supportedUsageFlags = VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    if (CanAddColorAttachment(swapchain, surface, format)) return 9;
    surface.supportedUsageFlags |= VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    swapchain.oldSwapchain = (VkSwapchainKHR)(uintptr_t)1;
    if (CanAddColorAttachment(swapchain, surface, format)) return 10;
    swapchain.oldSwapchain = VK_NULL_HANDLE;
    swapchain.pNext = &format;
    if (CanAddColorAttachment(swapchain, surface, format)) return 11;
    swapchain.pNext = nullptr;
    swapchain.flags = VK_SWAPCHAIN_CREATE_PROTECTED_BIT_KHR;
    if (CanAddColorAttachment(swapchain, surface, format)) return 12;
    swapchain.flags = 0;
    swapchain.imageArrayLayers = 2;
    if (CanAddColorAttachment(swapchain, surface, format)) return 13;
    swapchain.imageArrayLayers = 1;
    auto adjusted = swapchain;
    adjusted.imageUsage |= VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    const VkSwapchainCreateInfoKHR* applied = nullptr;
    if (phantom::vulkan_overlay::CreateSwapchainWithFallback(create_test_swapchain, VK_NULL_HANDLE,
            &swapchain, adjusted, true, nullptr, nullptr, applied) != VK_SUCCESS
        || swapchain_calls != 2 || first_usage != adjusted.imageUsage
        || second_usage != swapchain.imageUsage || applied != &swapchain) return 14;
    swapchain_calls = 0;
    swapchain.oldSwapchain = (VkSwapchainKHR)(uintptr_t)1;
    if (phantom::vulkan_overlay::CreateSwapchainWithFallback(create_test_swapchain, VK_NULL_HANDLE,
            &swapchain, adjusted, true, nullptr, nullptr, applied) != VK_ERROR_FORMAT_NOT_SUPPORTED
        || swapchain_calls != 1 || applied != &swapchain) return 15;
    Handle device{reinterpret_cast<void*>(0xABC)}, first{}, second{};
    VkCommandBuffer commands[]{reinterpret_cast<VkCommandBuffer>(&first), reinterpret_cast<VkCommandBuffer>(&second)};
    if (!phantom::vulkan_overlay::InitializeCommandDispatch(reinterpret_cast<VkDevice>(&device), commands, 2, set_loader_data)
        || callback_count != 2 || first.dispatch != device.dispatch || second.dispatch != device.dispatch) return 6;
    first.dispatch = nullptr;
    if (!phantom::vulkan_overlay::InitializeCommandDispatch(reinterpret_cast<VkDevice>(&device), commands, 1, nullptr)
        || first.dispatch != device.dispatch) return 7;
#if defined(_WIN32)
    const auto module = GetModuleHandleW(L"VkLayer_phantom_monitor.dll");
    if (!module || !GetProcAddress(module,"Phantom_vkGetInstanceProcAddr")
        || !GetProcAddress(module,"Phantom_vkGetDeviceProcAddr")
        || !GetProcAddress(module,"Phantom_vkNegotiateLoaderLayerInterfaceVersion")) return 4;
#endif
    VkNegotiateLayerInterface negotiation{};
    negotiation.sType = LAYER_NEGOTIATE_INTERFACE_STRUCT;
    negotiation.loaderLayerInterfaceVersion = 2;
    if (Phantom_vkNegotiateLoaderLayerInterfaceVersion(&negotiation) != VK_SUCCESS || !negotiation.pfnGetDeviceProcAddr) return 1;
    VkLayerInstanceLink link{}; link.pfnNextGetInstanceProcAddr = next_gipa;
    VkLayerInstanceCreateInfo chain{};
    chain.sType = VK_STRUCTURE_TYPE_LOADER_INSTANCE_CREATE_INFO; chain.function = VK_LAYER_LINK_INFO; chain.u.pLayerInfo = &link;
    VkInstanceCreateInfo info{}; info.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO; info.pNext = &chain;
    VkInstance handle{};
    auto create_instance = reinterpret_cast<PFN_vkCreateInstance>(Phantom_vkGetInstanceProcAddr(VK_NULL_HANDLE,"vkCreateInstance"));
    if (!create_instance || create_instance(&info,nullptr,&handle) != VK_SUCCESS) return 2;
    for (const auto name : {"vkGetDeviceProcAddr", "vkQueuePresentKHR", "vkGetDeviceQueue", "vkGetDeviceQueue2", "vkCreateSwapchainKHR", "vkDestroySwapchainKHR", "vkDestroyDevice"}) {
        const auto from_instance = Phantom_vkGetInstanceProcAddr(handle,name);
        const auto from_device = Phantom_vkGetDeviceProcAddr(VK_NULL_HANDLE,name);
        if (!from_instance || from_instance != from_device || from_instance == stub) { std::cerr << name << '\n'; return 3; }
    }
    queue2_available = false;
    if (Phantom_vkGetInstanceProcAddr(handle,"vkGetDeviceQueue2") != nullptr) return 5;
    reinterpret_cast<PFN_vkDestroyInstance>(Phantom_vkGetInstanceProcAddr(handle,"vkDestroyInstance"))(handle,nullptr);
    std::cout << "PASS: instance/device dispatch intercepts match and loader interface negotiates\n";
}
