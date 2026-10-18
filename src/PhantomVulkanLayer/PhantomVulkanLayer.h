#pragma once

#include <vulkan/vulkan.h>
#include <vulkan/vk_layer.h>
#include "PhantomVulkanTelemetry.h"

#if defined(_WIN32)
    #define PHANTOM_EXPORT __declspec(dllexport)
#else
    #define PHANTOM_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Pontos de entrada oficiais exigidos pelo Vulkan Loader
PHANTOM_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Phantom_vkGetInstanceProcAddr(
    VkInstance instance,
    const char* pName);

PHANTOM_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Phantom_vkGetDeviceProcAddr(
    VkDevice device,
    const char* pName);

PHANTOM_EXPORT VKAPI_ATTR VkResult VKAPI_CALL Phantom_vkNegotiateLoaderLayerInterfaceVersion(
    VkNegotiateLayerInterface* pVersionStruct);

#ifdef __cplusplus
}
#endif
