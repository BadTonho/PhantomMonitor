#include "PhantomVulkanOverlay.h"
#include "PhantomVulkanDispatch.h"
#include "PhantomSwapchainPolicy.h"

#include "OverlayFrameReader.h"
#include "OverlayImGuiDraw.h"
#include "OverlayStatus.h"
#include "imgui.h"
#include "imgui_impl_vulkan.h"

#include <algorithm>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <type_traits>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace phantom::vulkan_overlay {
namespace {

using phantom::overlay::Frame;
using phantom::overlay::FrameReader;
using phantom::overlay::RenderApi;
using phantom::overlay::RenderState;
using phantom::overlay::StatusProducer;
using phantom::overlay::StatusPublisher;
using phantom::overlay::DrawFrame;

struct DeviceFunctions {
    PFN_vkGetDeviceProcAddr gdpa = nullptr;
    PFN_vkGetSwapchainImagesKHR get_swapchain_images = nullptr;
    PFN_vkCreateImageView create_image_view = nullptr;
    PFN_vkDestroyImageView destroy_image_view = nullptr;
    PFN_vkCreateRenderPass create_render_pass = nullptr;
    PFN_vkDestroyRenderPass destroy_render_pass = nullptr;
    PFN_vkCreateFramebuffer create_framebuffer = nullptr;
    PFN_vkDestroyFramebuffer destroy_framebuffer = nullptr;
    PFN_vkCreateCommandPool create_command_pool = nullptr;
    PFN_vkDestroyCommandPool destroy_command_pool = nullptr;
    PFN_vkAllocateCommandBuffers allocate_command_buffers = nullptr;
    PFN_vkFreeCommandBuffers free_command_buffers = nullptr;
    PFN_vkResetCommandBuffer reset_command_buffer = nullptr;
    PFN_vkBeginCommandBuffer begin_command_buffer = nullptr;
    PFN_vkEndCommandBuffer end_command_buffer = nullptr;
    PFN_vkCmdBeginRenderPass cmd_begin_render_pass = nullptr;
    PFN_vkCmdEndRenderPass cmd_end_render_pass = nullptr;
    PFN_vkCreateSemaphore create_semaphore = nullptr;
    PFN_vkDestroySemaphore destroy_semaphore = nullptr;
    PFN_vkQueueSubmit queue_submit = nullptr;
    PFN_vkDeviceWaitIdle device_wait_idle = nullptr;
    PFN_vkCreateFence create_fence = nullptr;
    PFN_vkDestroyFence destroy_fence = nullptr;
    PFN_vkWaitForFences wait_for_fences = nullptr;
    PFN_vkResetFences reset_fences = nullptr;
};

struct SwapchainState {
    VkSwapchainKHR handle = VK_NULL_HANDLE;
    VkFormat format = VK_FORMAT_UNDEFINED;
    VkExtent2D extent{};
    std::uint32_t min_image_count = 0;
    std::vector<VkImage> images;
    std::vector<VkImageView> views;
    std::vector<VkFramebuffer> framebuffers;
    std::vector<VkCommandBuffer> commands;
    std::vector<VkSemaphore> render_complete;
    std::vector<VkFence> submitted;
    VkRenderPass render_pass = VK_NULL_HANDLE;
    VkCommandPool command_pool = VK_NULL_HANDLE;
    ImGuiContext* imgui = nullptr;
    FrameReader reader;
    bool initialized = false;
    bool supported = true;
    bool submission_failed = false;
    std::string unsupported_reason;
    std::uint32_t initialized_family = UINT32_MAX;
    std::string last_error;
    std::mutex render_mutex;
};

struct DeviceState {
    VkInstance instance = VK_NULL_HANDLE;
    VkPhysicalDevice physical = VK_NULL_HANDLE;
    VkDevice device = VK_NULL_HANDLE;
    DeviceFunctions fn;
    PFN_vkGetInstanceProcAddr gipa = nullptr;
    PFN_vkSetDeviceLoaderData set_loader_data = nullptr;
    VkAllocationCallbacks allocator_copy{};
    bool has_allocator = false;
    std::unordered_set<std::uint32_t> requested_families;
    std::unordered_map<std::uint32_t, bool> graphics_families;
    std::unordered_map<VkQueue, std::uint32_t> queue_families;
    std::unordered_map<std::uint64_t, std::shared_ptr<SwapchainState>> swapchains;
    std::mutex family_mutex;
};

std::mutex g_mutex;
// The ImGui backend's function pointers and current context are process-global.
std::mutex g_imgui_mutex;
std::unordered_map<void*, std::shared_ptr<DeviceState>> g_devices;
StatusPublisher g_status(StatusProducer::Vulkan);

void* dispatch_key(void* object) {
    return object ? *reinterpret_cast<void**>(object) : nullptr;
}

template<typename T>
std::uint64_t handle_key(T handle) {
    if constexpr (std::is_pointer_v<T>)
        return static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(handle));
    else
        return static_cast<std::uint64_t>(handle);
}

template<typename T>
T device_proc(PFN_vkGetDeviceProcAddr gdpa, VkDevice device, const char* name) {
    return reinterpret_cast<T>(gdpa ? gdpa(device, name) : nullptr);
}

bool query_graphics_family(DeviceState& state, std::uint32_t family) {
    std::lock_guard<std::mutex> lock(state.family_mutex);
    const auto known = state.graphics_families.find(family);
    if (known != state.graphics_families.end()) return known->second;
    if (state.requested_families.find(family) == state.requested_families.end()) {
        state.graphics_families[family] = false;
        return false;
    }

    auto get_properties = reinterpret_cast<PFN_vkGetPhysicalDeviceQueueFamilyProperties>(
        state.gipa(state.instance, "vkGetPhysicalDeviceQueueFamilyProperties"));
    if (!get_properties) {
        state.graphics_families[family] = false;
        return false;
    }
    std::uint32_t count = 0;
    get_properties(state.physical, &count, nullptr);
    if (family >= count) {
        state.graphics_families[family] = false;
        return false;
    }
    std::vector<VkQueueFamilyProperties> properties(count);
    get_properties(state.physical, &count, properties.data());
    const bool graphics = family < count && (properties[family].queueFlags & VK_QUEUE_GRAPHICS_BIT) != 0;
    state.graphics_families[family] = graphics;
    return graphics;
}

void update_status(RenderState state, const std::string& message) noexcept {
    try { g_status.Update(RenderApi::Vulkan, state, message); }
    catch (...) { }
}

bool load_backend_functions(DeviceState& device) {
    return ImGui_ImplVulkan_LoadFunctions(VK_API_VERSION_1_0,
        [](const char* name, void* user) -> PFN_vkVoidFunction {
            auto& state = *static_cast<DeviceState*>(user);
            // Instance/physical-device queries must use GIPA. Device functions
            // stay below this layer, including backend-owned command buffers.
            if (std::strstr(name, "PhysicalDevice") || std::strcmp(name, "vkEnumeratePhysicalDevices") == 0)
                return state.gipa(state.instance, name);
            auto result = state.fn.gdpa(state.device, name);
            return result ? result : state.gipa(state.instance, name);
        }, &device);
}

void destroy_swapchain(DeviceState& device, SwapchainState& swapchain) {
    load_backend_functions(device);
    if (swapchain.imgui != nullptr) {
        ImGui::SetCurrentContext(swapchain.imgui);
        if (swapchain.initialized) ImGui_ImplVulkan_Shutdown();
        ImGui::DestroyContext(swapchain.imgui);
        swapchain.imgui = nullptr;
    }
    for (const auto semaphore : swapchain.render_complete)
        if (semaphore && device.fn.destroy_semaphore)
            device.fn.destroy_semaphore(device.device, semaphore, nullptr);
    for (const auto fence : swapchain.submitted)
        if (fence && device.fn.destroy_fence) device.fn.destroy_fence(device.device, fence, nullptr);
    swapchain.submitted.clear();
    if (!swapchain.commands.empty() && swapchain.command_pool && device.fn.free_command_buffers)
        device.fn.free_command_buffers(device.device, swapchain.command_pool,
            static_cast<std::uint32_t>(swapchain.commands.size()), swapchain.commands.data());
    swapchain.commands.clear();
    if (swapchain.command_pool && device.fn.destroy_command_pool)
        device.fn.destroy_command_pool(device.device, swapchain.command_pool, nullptr);
    swapchain.command_pool = VK_NULL_HANDLE;
    for (const auto framebuffer : swapchain.framebuffers)
        if (framebuffer && device.fn.destroy_framebuffer)
            device.fn.destroy_framebuffer(device.device, framebuffer, nullptr);
    swapchain.framebuffers.clear();
    for (const auto view : swapchain.views)
        if (view && device.fn.destroy_image_view)
            device.fn.destroy_image_view(device.device, view, nullptr);
    swapchain.views.clear();
    if (swapchain.render_pass && device.fn.destroy_render_pass)
        device.fn.destroy_render_pass(device.device, swapchain.render_pass, nullptr);
    swapchain.render_pass = VK_NULL_HANDLE;
    swapchain.render_complete.clear();
    swapchain.images.clear();
    swapchain.initialized = false;
    swapchain.initialized_family = UINT32_MAX;
}

struct SwapchainInitializationGuard {
    DeviceState& device;
    SwapchainState& swapchain;
    bool committed = false;
    ~SwapchainInitializationGuard() {
        if (!committed) destroy_swapchain(device, swapchain);
    }
};

bool create_render_pass(DeviceState& device, SwapchainState& swapchain, VkResult& result) {
    VkAttachmentDescription attachment{};
    attachment.format = swapchain.format;
    attachment.samples = VK_SAMPLE_COUNT_1_BIT;
    attachment.loadOp = VK_ATTACHMENT_LOAD_OP_LOAD;
    attachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    attachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    attachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    attachment.initialLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
    attachment.finalLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;

    VkAttachmentReference color_reference{};
    color_reference.attachment = 0;
    color_reference.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    VkSubpassDescription subpass{};
    subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
    subpass.colorAttachmentCount = 1;
    subpass.pColorAttachments = &color_reference;

    VkSubpassDependency dependency{};
    dependency.srcSubpass = VK_SUBPASS_EXTERNAL;
    dependency.dstSubpass = 0;
    dependency.srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    dependency.dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    dependency.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    dependency.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    dependency.dependencyFlags = VK_DEPENDENCY_BY_REGION_BIT;

    VkRenderPassCreateInfo info{};
    info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
    info.attachmentCount = 1;
    info.pAttachments = &attachment;
    info.subpassCount = 1;
    info.pSubpasses = &subpass;
    info.dependencyCount = 1;
    info.pDependencies = &dependency;
    result = device.fn.create_render_pass(device.device, &info, nullptr, &swapchain.render_pass);
    return result == VK_SUCCESS;
}

bool initialize_swapchain(DeviceState& device, SwapchainState& swapchain,
                          std::uint32_t queue_family, VkQueue queue) {
    if (swapchain.initialized) return true;
    SwapchainInitializationGuard cleanup{ device, swapchain };
    if (!device.fn.get_swapchain_images || !device.fn.create_image_view || !device.fn.create_render_pass
        || !device.fn.create_framebuffer || !device.fn.create_command_pool || !device.fn.allocate_command_buffers
        || !device.fn.create_semaphore || !device.fn.reset_command_buffer || !device.fn.begin_command_buffer
        || !device.fn.end_command_buffer || !device.fn.cmd_begin_render_pass || !device.fn.cmd_end_render_pass
        || !device.fn.queue_submit || !device.fn.create_fence || !device.fn.destroy_fence
        || !device.fn.wait_for_fences || !device.fn.reset_fences) {
        swapchain.last_error = "Vulkan loader did not provide all rendering entry points";
        return false;
    }
    if (swapchain.images.empty()) {
        std::uint32_t count = 0;
        VkResult result = device.fn.get_swapchain_images(device.device, swapchain.handle, &count, nullptr);
        if (result != VK_SUCCESS || count < 2) {
            swapchain.last_error = "Vulkan swapchain has fewer than two images or image enumeration failed";
            return false;
        }
        swapchain.images.resize(count);
        result = device.fn.get_swapchain_images(device.device, swapchain.handle, &count, swapchain.images.data());
        if (result != VK_SUCCESS && result != VK_INCOMPLETE) {
            swapchain.last_error = "vkGetSwapchainImagesKHR failed";
            return false;
        }
        swapchain.images.resize(count);
    }

    VkResult result = VK_SUCCESS;
    if (!create_render_pass(device, swapchain, result)) {
        swapchain.last_error = "vkCreateRenderPass failed: " + std::to_string(result);
        return false;
    }
    constexpr const VkAllocationCallbacks* allocator = nullptr;
    swapchain.views.reserve(swapchain.images.size());
    swapchain.framebuffers.reserve(swapchain.images.size());
    swapchain.render_complete.reserve(swapchain.images.size());
    for (const auto image : swapchain.images) {
        VkImageViewCreateInfo view_info{};
        view_info.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        view_info.image = image;
        view_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
        view_info.format = swapchain.format;
        view_info.components = { VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY,
            VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY };
        view_info.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        view_info.subresourceRange.levelCount = 1;
        view_info.subresourceRange.layerCount = 1;
        VkImageView view = VK_NULL_HANDLE;
        result = device.fn.create_image_view(device.device, &view_info, allocator, &view);
        if (result != VK_SUCCESS) {
            swapchain.last_error = "vkCreateImageView failed: " + std::to_string(result);
            return false;
        }
        swapchain.views.push_back(view);

        VkFramebufferCreateInfo framebuffer_info{};
        framebuffer_info.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
        framebuffer_info.renderPass = swapchain.render_pass;
        framebuffer_info.attachmentCount = 1;
        framebuffer_info.pAttachments = &swapchain.views.back();
        framebuffer_info.width = swapchain.extent.width;
        framebuffer_info.height = swapchain.extent.height;
        framebuffer_info.layers = 1;
        VkFramebuffer framebuffer = VK_NULL_HANDLE;
        result = device.fn.create_framebuffer(device.device, &framebuffer_info, allocator, &framebuffer);
        if (result != VK_SUCCESS) {
            swapchain.last_error = "vkCreateFramebuffer failed: " + std::to_string(result);
            return false;
        }
        swapchain.framebuffers.push_back(framebuffer);

        VkSemaphoreCreateInfo semaphore_info{};
        semaphore_info.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
        VkSemaphore semaphore = VK_NULL_HANDLE;
        result = device.fn.create_semaphore(device.device, &semaphore_info, allocator, &semaphore);
        if (result != VK_SUCCESS) {
            swapchain.last_error = "vkCreateSemaphore failed: " + std::to_string(result);
            return false;
        }
        swapchain.render_complete.push_back(semaphore);
        VkFenceCreateInfo fence_info{};
        fence_info.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        fence_info.flags = VK_FENCE_CREATE_SIGNALED_BIT;
        VkFence fence = VK_NULL_HANDLE;
        result = device.fn.create_fence(device.device, &fence_info, allocator, &fence);
        if (result != VK_SUCCESS) {
            swapchain.last_error = "vkCreateFence failed";
            return false;
        }
        swapchain.submitted.push_back(fence);
    }

    VkCommandPoolCreateInfo pool_info{};
    pool_info.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    pool_info.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    pool_info.queueFamilyIndex = queue_family;
    result = device.fn.create_command_pool(device.device, &pool_info, allocator, &swapchain.command_pool);
    if (result != VK_SUCCESS) {
        swapchain.last_error = "vkCreateCommandPool failed: " + std::to_string(result);
        return false;
    }
    swapchain.commands.resize(swapchain.images.size());
    VkCommandBufferAllocateInfo command_info{};
    command_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    command_info.commandPool = swapchain.command_pool;
    command_info.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    command_info.commandBufferCount = static_cast<std::uint32_t>(swapchain.commands.size());
    result = device.fn.allocate_command_buffers(device.device, &command_info, swapchain.commands.data());
    if (result != VK_SUCCESS) {
        swapchain.last_error = "vkAllocateCommandBuffers failed: " + std::to_string(result);
        return false;
    }
    if (!InitializeCommandDispatch(device.device, swapchain.commands.data(),
            static_cast<std::uint32_t>(swapchain.commands.size()), device.set_loader_data)) {
        swapchain.last_error = "Vulkan loader rejected command buffer dispatch initialization";
        return false;
    }

    swapchain.imgui = ImGui::CreateContext();
    if (swapchain.imgui == nullptr) {
        swapchain.last_error = "Dear ImGui context could not be created";
        return false;
    }
    ImGui::SetCurrentContext(swapchain.imgui);
    phantom::overlay::ConfigureOverlayFonts();
    ImGui_ImplVulkan_InitInfo imgui_info{};
    imgui_info.ApiVersion = VK_API_VERSION_1_0;
    imgui_info.Instance = device.instance;
    imgui_info.PhysicalDevice = device.physical;
    imgui_info.Device = device.device;
    imgui_info.QueueFamily = queue_family;
    imgui_info.Queue = queue;
    imgui_info.MinImageCount = std::max(2U, swapchain.min_image_count);
    imgui_info.ImageCount = static_cast<std::uint32_t>(swapchain.images.size());
    imgui_info.MSAASamples = VK_SAMPLE_COUNT_1_BIT;
    imgui_info.DescriptorPoolSize = 16;
    imgui_info.RenderPass = swapchain.render_pass;
    imgui_info.Allocator = allocator;
    if (!ImGui_ImplVulkan_Init(&imgui_info)) {
        swapchain.last_error = "Dear ImGui Vulkan backend initialization failed";
        return false;
    }
    swapchain.initialized = true;
    swapchain.initialized_family = queue_family;
    if (!ImGui_ImplVulkan_CreateFontsTexture()) {
        swapchain.last_error = "Dear ImGui font texture upload failed";
        return false;
    }
    swapchain.last_error.clear();
    cleanup.committed = true;
    update_status(RenderState::Waiting, "Vulkan renderer pronto; aguardando um quadro aplicado do PhantomMonitor.");
    return true;
}

bool draw_present(DeviceState& device, SwapchainState& swapchain, VkQueue queue,
                  std::uint32_t queue_family, const VkPresentInfoKHR* present_info,
                  PFN_vkQueuePresentKHR next_present, VkResult* result_out) {
    if (!swapchain.supported || swapchain.submission_failed || (swapchain.initialized && swapchain.initialized_family != queue_family)) {
        const auto reason = !swapchain.supported ? swapchain.unsupported_reason
            : swapchain.submission_failed ? "Uma submissão anterior do overlay Vulkan falhou."
            : "A fila de apresentação Vulkan mudou de família após inicializar o overlay.";
        update_status(RenderState::Unsupported, reason);
        return false;
    }
    if (!load_backend_functions(device)) {
        update_status(RenderState::Unsupported, "A cadeia Vulkan não oferece as funções exigidas pelo renderer.");
        return false;
    }
    if (present_info->swapchainCount != 1 || present_info->pImageIndices == nullptr) {
        update_status(RenderState::Unsupported, "Vulkan presenta múltiplas swapchains no mesmo quadro; o overlay foi ignorado.");
        return false;
    }
    Frame frame;
    std::string error;
    if (!swapchain.reader.TryRead(frame, error)) {
        const auto stale = error == "frame is stale";
        update_status(stale ? RenderState::Stale : RenderState::Waiting,
            stale ? "Quadro compartilhado expirou; nenhum conteúdo foi desenhado."
                  : "Aguardando quadro compartilhado válido: " + error);
        return false;
    }
    if (!frame.visible) {
        update_status(RenderState::Hidden, "Overlay dentro do jogo oculto pelo PhantomMonitor.");
        return false;
    }
    if (!swapchain.initialized && !initialize_swapchain(device, swapchain, queue_family, queue)) {
        update_status(RenderState::Failed, swapchain.last_error);
        return false;
    }
    const auto image_index = present_info->pImageIndices[0];
    if (image_index >= swapchain.images.size() || image_index >= swapchain.render_complete.size()) {
        update_status(RenderState::Failed, "Índice de imagem da swapchain Vulkan inválido.");
        return false;
    }
    if (swapchain.render_complete[image_index] == VK_NULL_HANDLE) {
        update_status(RenderState::Failed, "Semáforo do renderer Vulkan indisponível; overlay ignorado.");
        return false;
    }

    // Acquiring an image index need not mean its acquire semaphore has already
    // signaled. Drain our previous submission before resetting any CPU resources.
    if (device.fn.wait_for_fences(device.device, 1, &swapchain.submitted[image_index], VK_TRUE, 1'000'000'000) != VK_SUCCESS) {
        update_status(RenderState::Failed, "Timeout aguardando comandos Vulkan anteriores; apresentação preservada.");
        return false;
    }
    ImGui::SetCurrentContext(swapchain.imgui);
    ImGui_ImplVulkan_SetFrameIndex(image_index);
    ImGui_ImplVulkan_NewFrame();
    ImDrawData* draw_data = nullptr;
    DrawFrame(frame, swapchain.extent.width, swapchain.extent.height,
        static_cast<float>(swapchain.extent.width) / frame.reference_width,
        static_cast<float>(swapchain.extent.height) / frame.reference_height, &draw_data);
    if (draw_data == nullptr) {
        update_status(RenderState::Hidden, "Quadro nativo sem conteúdo visível.");
        return false;
    }

    auto command_buffer = swapchain.commands[image_index];
    VkResult result = device.fn.reset_command_buffer(command_buffer, 0);
    if (result != VK_SUCCESS) {
        update_status(RenderState::Failed, "vkResetCommandBuffer falhou: " + std::to_string(result));
        return false;
    }
    VkCommandBufferBeginInfo begin_info{};
    begin_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    begin_info.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    result = device.fn.begin_command_buffer(command_buffer, &begin_info);
    if (result != VK_SUCCESS) {
        update_status(RenderState::Failed, "vkBeginCommandBuffer falhou: " + std::to_string(result));
        return false;
    }
    VkRenderPassBeginInfo render_info{};
    render_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    render_info.renderPass = swapchain.render_pass;
    render_info.framebuffer = swapchain.framebuffers[image_index];
    render_info.renderArea.extent = swapchain.extent;
    device.fn.cmd_begin_render_pass(command_buffer, &render_info, VK_SUBPASS_CONTENTS_INLINE);
    ImGui_ImplVulkan_RenderDrawData(draw_data, command_buffer);
    device.fn.cmd_end_render_pass(command_buffer);
    result = device.fn.end_command_buffer(command_buffer);
    if (result != VK_SUCCESS) {
        update_status(RenderState::Failed, "vkEndCommandBuffer falhou: " + std::to_string(result));
        return false;
    }

    std::vector<VkPipelineStageFlags> wait_stages(present_info->waitSemaphoreCount,
        VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT);
    VkSubmitInfo submit{};
    submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submit.waitSemaphoreCount = present_info->waitSemaphoreCount;
    submit.pWaitSemaphores = present_info->pWaitSemaphores;
    submit.pWaitDstStageMask = wait_stages.empty() ? nullptr : wait_stages.data();
    submit.commandBufferCount = 1;
    submit.pCommandBuffers = &command_buffer;
    submit.signalSemaphoreCount = 1;
    submit.pSignalSemaphores = &swapchain.render_complete[image_index];
    result = device.fn.reset_fences(device.device, 1, &swapchain.submitted[image_index]);
    if (result != VK_SUCCESS) return false;
    result = device.fn.queue_submit(queue, 1, &submit, swapchain.submitted[image_index]);
    if (result != VK_SUCCESS) {
        swapchain.submission_failed = true;
        update_status(RenderState::Failed, "vkQueueSubmit do overlay falhou: " + std::to_string(result));
        return false;
    }

    VkPresentInfoKHR overlay_present = *present_info;
    overlay_present.waitSemaphoreCount = 1;
    overlay_present.pWaitSemaphores = &swapchain.render_complete[image_index];
    *result_out = next_present(queue, &overlay_present);
    if (*result_out == VK_SUCCESS || *result_out == VK_SUBOPTIMAL_KHR)
        update_status(RenderState::Active, "Renderer Vulkan desenhando a apresentação aplicada.");
    else {
        update_status(RenderState::Failed, "vkQueuePresentKHR após desenho falhou: " + std::to_string(*result_out));
        // OUT_OF_DATE/SURFACE_LOST can leave the presentation wait unscheduled.
        // Drain the queue before replacing the binary semaphore so it can never
        // be signaled again while still signaled from the previous submit.
        if (device.fn.device_wait_idle) device.fn.device_wait_idle(device.device);
        if (device.fn.destroy_semaphore)
            device.fn.destroy_semaphore(device.device, swapchain.render_complete[image_index], nullptr);
        swapchain.render_complete[image_index] = VK_NULL_HANDLE;
        VkSemaphoreCreateInfo semaphore_info{};
        semaphore_info.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
        if (device.fn.create_semaphore
            && device.fn.create_semaphore(device.device, &semaphore_info, nullptr,
                &swapchain.render_complete[image_index]) != VK_SUCCESS)
            swapchain.render_complete[image_index] = VK_NULL_HANDLE;
    }
    return true;
}

std::shared_ptr<DeviceState> find_device(VkDevice device) {
    std::lock_guard<std::mutex> lock(g_mutex);
    const auto found = g_devices.find(dispatch_key(device));
    return found == g_devices.end() ? nullptr : found->second;
}

} // namespace

void RegisterDevice(VkInstance instance, VkPhysicalDevice physical_device, VkDevice device,
                    const VkDeviceCreateInfo* create_info,
                    PFN_vkGetInstanceProcAddr next_gipa,
                    PFN_vkGetDeviceProcAddr next_gdpa,
                    PFN_vkSetDeviceLoaderData set_loader_data) {
    if (!device || !next_gdpa) return;
    auto state = std::make_shared<DeviceState>();
    state->instance = instance;
    state->physical = physical_device;
    state->device = device;
    state->fn.gdpa = next_gdpa;
    state->gipa = next_gipa;
    state->set_loader_data = set_loader_data;
    state->fn.get_swapchain_images = device_proc<PFN_vkGetSwapchainImagesKHR>(next_gdpa, device, "vkGetSwapchainImagesKHR");
    state->fn.create_image_view = device_proc<PFN_vkCreateImageView>(next_gdpa, device, "vkCreateImageView");
    state->fn.destroy_image_view = device_proc<PFN_vkDestroyImageView>(next_gdpa, device, "vkDestroyImageView");
    state->fn.create_render_pass = device_proc<PFN_vkCreateRenderPass>(next_gdpa, device, "vkCreateRenderPass");
    state->fn.destroy_render_pass = device_proc<PFN_vkDestroyRenderPass>(next_gdpa, device, "vkDestroyRenderPass");
    state->fn.create_framebuffer = device_proc<PFN_vkCreateFramebuffer>(next_gdpa, device, "vkCreateFramebuffer");
    state->fn.destroy_framebuffer = device_proc<PFN_vkDestroyFramebuffer>(next_gdpa, device, "vkDestroyFramebuffer");
    state->fn.create_command_pool = device_proc<PFN_vkCreateCommandPool>(next_gdpa, device, "vkCreateCommandPool");
    state->fn.destroy_command_pool = device_proc<PFN_vkDestroyCommandPool>(next_gdpa, device, "vkDestroyCommandPool");
    state->fn.allocate_command_buffers = device_proc<PFN_vkAllocateCommandBuffers>(next_gdpa, device, "vkAllocateCommandBuffers");
    state->fn.free_command_buffers = device_proc<PFN_vkFreeCommandBuffers>(next_gdpa, device, "vkFreeCommandBuffers");
    state->fn.reset_command_buffer = device_proc<PFN_vkResetCommandBuffer>(next_gdpa, device, "vkResetCommandBuffer");
    state->fn.begin_command_buffer = device_proc<PFN_vkBeginCommandBuffer>(next_gdpa, device, "vkBeginCommandBuffer");
    state->fn.end_command_buffer = device_proc<PFN_vkEndCommandBuffer>(next_gdpa, device, "vkEndCommandBuffer");
    state->fn.cmd_begin_render_pass = device_proc<PFN_vkCmdBeginRenderPass>(next_gdpa, device, "vkCmdBeginRenderPass");
    state->fn.cmd_end_render_pass = device_proc<PFN_vkCmdEndRenderPass>(next_gdpa, device, "vkCmdEndRenderPass");
    state->fn.create_semaphore = device_proc<PFN_vkCreateSemaphore>(next_gdpa, device, "vkCreateSemaphore");
    state->fn.destroy_semaphore = device_proc<PFN_vkDestroySemaphore>(next_gdpa, device, "vkDestroySemaphore");
    state->fn.queue_submit = device_proc<PFN_vkQueueSubmit>(next_gdpa, device, "vkQueueSubmit");
    state->fn.device_wait_idle = device_proc<PFN_vkDeviceWaitIdle>(next_gdpa, device, "vkDeviceWaitIdle");
    state->fn.create_fence = device_proc<PFN_vkCreateFence>(next_gdpa, device, "vkCreateFence");
    state->fn.destroy_fence = device_proc<PFN_vkDestroyFence>(next_gdpa, device, "vkDestroyFence");
    state->fn.wait_for_fences = device_proc<PFN_vkWaitForFences>(next_gdpa, device, "vkWaitForFences");
    state->fn.reset_fences = device_proc<PFN_vkResetFences>(next_gdpa, device, "vkResetFences");
    if (create_info != nullptr) {
        for (std::uint32_t i = 0; i < create_info->queueCreateInfoCount; ++i)
            state->requested_families.insert(create_info->pQueueCreateInfos[i].queueFamilyIndex);
    }
    std::lock_guard<std::mutex> lock(g_mutex);
    g_devices[dispatch_key(device)] = state;
    update_status(RenderState::Waiting, "Camada Vulkan ativa; aguardando swapchain e apresentação do jogo.");
}

void RegisterQueue(VkDevice device, VkQueue queue, std::uint32_t queue_family) {
    auto state = find_device(device);
    if (!state || !queue) return;
    std::lock_guard<std::mutex> lock(g_mutex);
    state->queue_families[queue] = queue_family;
}

bool PrepareSwapchainForOverlay(VkDevice device, const VkSwapchainCreateInfoKHR* original,
                                VkSwapchainCreateInfoKHR& adjusted) {
    if (!original || original->surface == VK_NULL_HANDLE || original->oldSwapchain != VK_NULL_HANDLE || original->pNext != nullptr
        || original->flags != 0 || original->imageArrayLayers != 1
        || (original->imageUsage & VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT) != 0) return false;
    const auto state = find_device(device);
    if (!state || !state->gipa || !state->physical) return false;
    const auto get_surface = reinterpret_cast<PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR>(
        state->gipa(state->instance, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR"));
    const auto get_format = reinterpret_cast<PFN_vkGetPhysicalDeviceImageFormatProperties>(
        state->gipa(state->instance, "vkGetPhysicalDeviceImageFormatProperties"));
    if (!get_surface || !get_format) return false;
    VkSurfaceCapabilitiesKHR surface{};
    VkImageFormatProperties format{};
    const auto usage = original->imageUsage | VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    if (get_surface(state->physical, original->surface, &surface) != VK_SUCCESS
        || get_format(state->physical, original->imageFormat, VK_IMAGE_TYPE_2D,
                      VK_IMAGE_TILING_OPTIMAL, usage, 0, &format) != VK_SUCCESS
        || !CanAddColorAttachment(*original, surface, format)) return false;
    adjusted = *original;
    adjusted.imageUsage = usage;
    return true;
}

void RegisterSwapchain(VkDevice device, VkSwapchainKHR swapchain,
                       const VkSwapchainCreateInfoKHR* create_info,
                       const VkAllocationCallbacks* allocator) {
    auto state = find_device(device);
    if (!state || !swapchain || !create_info) return;
    auto entry = std::make_shared<SwapchainState>();
    entry->handle = swapchain;
    entry->format = create_info->imageFormat;
    entry->extent = create_info->imageExtent;
    entry->min_image_count = create_info->minImageCount;
    entry->supported = (create_info->imageUsage & VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT) != 0
        && create_info->imageArrayLayers == 1
        && (create_info->flags & VK_SWAPCHAIN_CREATE_PROTECTED_BIT_KHR) == 0;
    if ((create_info->flags & VK_SWAPCHAIN_CREATE_PROTECTED_BIT_KHR) != 0)
        entry->unsupported_reason = "Swapchain Vulkan protegida; o overlay não pode desenhar nela.";
    else if (create_info->imageArrayLayers != 1)
        entry->unsupported_reason = "Swapchain Vulkan com múltiplas camadas não é suportada pelo overlay.";
    else if ((create_info->imageUsage & VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT) == 0)
        entry->unsupported_reason = "Swapchain Vulkan sem uso color attachment; não foi possível habilitá-lo com segurança.";
    (void)allocator; // Renderer-owned objects always use Vulkan's default allocator.
    std::lock_guard<std::mutex> lock(g_mutex);
    state->swapchains[handle_key(swapchain)] = std::move(entry);
}

void DestroySwapchain(VkDevice device, VkSwapchainKHR swapchain,
                      const VkAllocationCallbacks* allocator) {
    auto state = find_device(device);
    if (!state) return;
    std::shared_ptr<SwapchainState> entry;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        auto found = state->swapchains.find(handle_key(swapchain));
        if (found == state->swapchains.end()) return;
        entry = std::move(found->second);
        state->swapchains.erase(found);
    }
    if (state->fn.device_wait_idle) state->fn.device_wait_idle(device);
    (void)allocator;
    std::lock_guard<std::mutex> imgui_lock(g_imgui_mutex);
    std::lock_guard<std::mutex> render_lock(entry->render_mutex);
    destroy_swapchain(*state, *entry);
}

void DestroyDevice(VkDevice device) {
    std::shared_ptr<DeviceState> state;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        const auto found = g_devices.find(dispatch_key(device));
        if (found == g_devices.end()) return;
        state = std::move(found->second);
        g_devices.erase(found);
    }
    if (state->fn.device_wait_idle) state->fn.device_wait_idle(device);
    std::lock_guard<std::mutex> imgui_lock(g_imgui_mutex);
    for (auto& item : state->swapchains)
        destroy_swapchain(*state, *item.second);
    state->swapchains.clear();
}

bool TryPresent(VkDevice device, VkQueue queue, const VkPresentInfoKHR* present_info,
                PFN_vkQueuePresentKHR next_present, VkResult* result) {
    if (!present_info || !next_present || !result) return false;
    auto state = find_device(device);
    if (!state) return false;
    std::uint32_t queue_family = 0;
    std::shared_ptr<SwapchainState> swapchain;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        const auto queue_found = state->queue_families.find(queue);
        if (queue_found == state->queue_families.end()) {
            update_status(RenderState::Unsupported, "Fila Vulkan de apresentação não registrada por vkGetDeviceQueue/2.");
            return false;
        }
        queue_family = queue_found->second;
        if (present_info->swapchainCount != 1 || present_info->pSwapchains == nullptr) {
            update_status(RenderState::Unsupported, "O renderer Vulkan suporta uma swapchain por chamada de apresentação.");
            return false;
        }
        auto found = state->swapchains.end();
        found = state->swapchains.find(handle_key(present_info->pSwapchains[0]));
        if (found == state->swapchains.end()) {
            update_status(RenderState::Waiting, "A apresentação Vulkan ainda não tem uma swapchain registrada.");
            return false;
        }
        swapchain = found->second;
    }
    if (!query_graphics_family(*state, queue_family)) {
        update_status(RenderState::Unsupported, "A fila Vulkan de apresentação não aceita comandos gráficos; overlay ignorado.");
        return false;
    }
    std::lock_guard<std::mutex> imgui_lock(g_imgui_mutex);
    std::lock_guard<std::mutex> render_lock(swapchain->render_mutex);
    return draw_present(*state, *swapchain, queue, queue_family, present_info, next_present, result);
}

} // namespace phantom::vulkan_overlay
