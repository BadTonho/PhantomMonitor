#pragma once

#include <vulkan/vulkan.h>
#include <vulkan/vk_layer.h>
#include "RivaVulkanTelemetry.h"

#if defined(_WIN32)
    #define RIVA_EXPORT __declspec(dllexport)
#else
    #define RIVA_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Pontos de entrada oficiais exigidos pelo Vulkan Loader
RIVA_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Riva_vkGetInstanceProcAddr(
    VkInstance instance,
    const char* pName);

RIVA_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Riva_vkGetDeviceProcAddr(
    VkDevice device,
    const char* pName);

RIVA_EXPORT VKAPI_ATTR VkResult VKAPI_CALL Riva_vkNegotiateLoaderLayerInterfaceVersion(
    VkNegotiateLayerInterface* pVersionStruct);

#ifdef __cplusplus
}
#endif
