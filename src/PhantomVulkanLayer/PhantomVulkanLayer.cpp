#include "PhantomVulkanLayer.h"
#include <cstring>
#include <ctime>
#include <unordered_map>
#include <mutex>
#include <atomic>
#include <string>

#if !defined(_WIN32)
#include <unistd.h>
#include <fcntl.h>
#include <sys/mman.h>
#include <sys/stat.h>
#else
#include <windows.h>
#endif

namespace {

// Tabela de despacho para funções de instância
struct InstanceDispatch {
    PFN_vkGetInstanceProcAddr get_instance_proc_addr = nullptr;
    PFN_vkDestroyInstance destroy_instance = nullptr;
};

// Tabela de despacho para funções de dispositivo
struct DeviceDispatch {
    PFN_vkGetDeviceProcAddr get_device_proc_addr = nullptr;
    PFN_vkDestroyDevice destroy_device = nullptr;
    PFN_vkQueuePresentKHR queue_present_khr = nullptr;
};

std::mutex g_mutex;
std::unordered_map<void*, InstanceDispatch> g_instance_dispatch;
std::unordered_map<void*, DeviceDispatch> g_device_dispatch;

// Telemetria e memória compartilhada
std::atomic<PhantomVulkanTelemetryHeader*> g_telemetry_shm{nullptr};
int g_shm_fd = -1;
std::atomic<uint64_t> g_frame_counter{0};
std::atomic<uint64_t> g_last_present_ns{0};

uint64_t get_time_ns() {
#if !defined(_WIN32)
    struct timespec ts{};
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return static_cast<uint64_t>(ts.tv_sec) * 1000000000ULL + static_cast<uint64_t>(ts.tv_nsec);
#else
    LARGE_INTEGER freq, counter;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&counter);
    return static_cast<uint64_t>((counter.QuadPart * 1000000000.0) / freq.QuadPart);
#endif
}

std::mutex g_shm_mutex;

void init_shared_memory() {
    std::lock_guard<std::mutex> lock(g_shm_mutex);
    if (g_telemetry_shm.load() != nullptr) return;

#if !defined(_WIN32)
    // 0600: apenas o mesmo usuário (jogo e PhantomMonitor) acessa a telemetria.
    // 1. Tentar POSIX shared memory (/dev/shm/phantom_fps)
    g_shm_fd = shm_open(PHANTOM_SHM_NAME, O_CREAT | O_RDWR, 0600);
    if (g_shm_fd < 0) {
        // Fallback para arquivo comum em /tmp/phantom_fps.bin
        g_shm_fd = open(PHANTOM_FALLBACK_FILE, O_CREAT | O_RDWR, 0600);
    }

    if (g_shm_fd >= 0) {
        size_t size = sizeof(PhantomVulkanTelemetryHeader);
        if (ftruncate(g_shm_fd, size) == 0) {
            void* addr = mmap(nullptr, size, PROT_READ | PROT_WRITE, MAP_SHARED, g_shm_fd, 0);
            if (addr != MAP_FAILED) {
                auto* shm = static_cast<PhantomVulkanTelemetryHeader*>(addr);
                std::memset(shm, 0, sizeof(PhantomVulkanTelemetryHeader));
                shm->magic = PHANTOM_TELEMETRY_MAGIC;
                shm->version = PHANTOM_TELEMETRY_VERSION;
                shm->pid = static_cast<uint32_t>(getpid());

                // Ler nome do processo em /proc/self/comm
                int comm_fd = open("/proc/self/comm", O_RDONLY);
                if (comm_fd >= 0) {
                    char comm_buf[64] = {0};
                    ssize_t bytes_read = read(comm_fd, comm_buf, sizeof(comm_buf) - 1);
                    if (bytes_read > 0) {
                        if (comm_buf[bytes_read - 1] == '\n') comm_buf[bytes_read - 1] = '\0';
                        std::strncpy(shm->process_name, comm_buf, sizeof(shm->process_name) - 1);
                    }
                    close(comm_fd);
                }

                // Publica o ponteiro só depois de inicializado.
                g_telemetry_shm.store(shm);
            }
        }
    }
#endif
}

void cleanup_shared_memory() {
    std::lock_guard<std::mutex> lock(g_shm_mutex);
#if !defined(_WIN32)
    // Zera o ponteiro antes de desmapear para que o present não escreva em memória liberada.
    PhantomVulkanTelemetryHeader* shm = g_telemetry_shm.exchange(nullptr);
    if (shm != nullptr) {
        munmap(shm, sizeof(PhantomVulkanTelemetryHeader));
    }
    if (g_shm_fd >= 0) {
        close(g_shm_fd);
        g_shm_fd = -1;
    }
#endif
}

// Chave do dispatch para ponteiros Vulkan (dispatchable handle)
void* get_dispatch_key(void* object) {
    return *reinterpret_cast<void**>(object);
}

} // namespace

// Interceptação de vkQueuePresentKHR: o coração da medição de quadros
static VKAPI_ATTR VkResult VKAPI_CALL Phantom_vkQueuePresentKHR(
    VkQueue queue,
    const VkPresentInfoKHR* pPresentInfo)
{
    void* key = get_dispatch_key(queue);
    PFN_vkQueuePresentKHR next_present = nullptr;

    {
        std::lock_guard<std::mutex> lock(g_mutex);
        auto it = g_device_dispatch.find(key);
        if (it != g_device_dispatch.end()) {
            next_present = it->second.queue_present_khr;
        }
    }

    // exchange atômico: seguro com vários threads/filas chamando present ao mesmo tempo.
    uint64_t now_ns = get_time_ns();
    uint64_t prev_ns = g_last_present_ns.exchange(now_ns);
    if (prev_ns > 0 && now_ns > prev_ns) {
        uint64_t delta_ns = now_ns - prev_ns;
        float ft_ms = static_cast<float>(delta_ns) / 1000000.0f;
        float fps = (ft_ms > 0.001f) ? (1000.0f / ft_ms) : 0.0f;

        // Lê o ponteiro uma única vez (cleanup pode zerá-lo em outro thread).
        PhantomVulkanTelemetryHeader* shm = g_telemetry_shm.load();
        if (shm != nullptr) {
            uint64_t frame = ++g_frame_counter;
            shm->frame_count = frame;
            shm->timestamp_ns = now_ns;
            shm->frametime_ms = ft_ms;
            shm->instant_fps = fps;
        }
    } else if (prev_ns == 0) {
        init_shared_memory();
    }
    if (next_present != nullptr) {
        return next_present(queue, pPresentInfo);
    }
    return VK_SUCCESS;
}

static VKAPI_ATTR VkResult VKAPI_CALL Phantom_vkCreateInstance(
    const VkInstanceCreateInfo* pCreateInfo,
    const VkAllocationCallbacks* pAllocator,
    VkInstance* pInstance)
{
    init_shared_memory();

    auto* layer_info = reinterpret_cast<const VkLayerInstanceCreateInfo*>(pCreateInfo->pNext);
    while (layer_info && (layer_info->sType != VK_STRUCTURE_TYPE_LOADER_INSTANCE_CREATE_INFO ||
                          layer_info->function != VK_LAYER_LINK_INFO)) {
        layer_info = reinterpret_cast<const VkLayerInstanceCreateInfo*>(layer_info->pNext);
    }

    if (!layer_info) return VK_ERROR_INITIALIZATION_FAILED;

    PFN_vkGetInstanceProcAddr next_gipa = layer_info->u.pLayerInfo->pfnNextGetInstanceProcAddr;
    PFN_vkCreateInstance next_create = reinterpret_cast<PFN_vkCreateInstance>(
        next_gipa(VK_NULL_HANDLE, "vkCreateInstance"));

    // Avançar chain
    const_cast<VkLayerInstanceCreateInfo*>(layer_info)->u.pLayerInfo =
        layer_info->u.pLayerInfo->pNext;

    VkResult res = next_create(pCreateInfo, pAllocator, pInstance);
    if (res == VK_SUCCESS && pInstance && *pInstance) {
        std::lock_guard<std::mutex> lock(g_mutex);
        void* key = get_dispatch_key(*pInstance);
        InstanceDispatch disp;
        disp.get_instance_proc_addr = next_gipa;
        disp.destroy_instance = reinterpret_cast<PFN_vkDestroyInstance>(
            next_gipa(*pInstance, "vkDestroyInstance"));
        g_instance_dispatch[key] = disp;
    }
    return res;
}

static VKAPI_ATTR void VKAPI_CALL Phantom_vkDestroyInstance(
    VkInstance instance,
    const VkAllocationCallbacks* pAllocator)
{
    void* key = get_dispatch_key(instance);
    PFN_vkDestroyInstance next_destroy = nullptr;

    {
        std::lock_guard<std::mutex> lock(g_mutex);
        auto it = g_instance_dispatch.find(key);
        if (it != g_instance_dispatch.end()) {
            next_destroy = it->second.destroy_instance;
            g_instance_dispatch.erase(it);
        }
    }

    cleanup_shared_memory();

    if (next_destroy) {
        next_destroy(instance, pAllocator);
    }
}

static VKAPI_ATTR VkResult VKAPI_CALL Phantom_vkCreateDevice(
    VkPhysicalDevice physicalDevice,
    const VkDeviceCreateInfo* pCreateInfo,
    const VkAllocationCallbacks* pAllocator,
    VkDevice* pDevice)
{
    auto* layer_info = reinterpret_cast<const VkLayerDeviceCreateInfo*>(pCreateInfo->pNext);
    while (layer_info && (layer_info->sType != VK_STRUCTURE_TYPE_LOADER_DEVICE_CREATE_INFO ||
                          layer_info->function != VK_LAYER_LINK_INFO)) {
        layer_info = reinterpret_cast<const VkLayerDeviceCreateInfo*>(layer_info->pNext);
    }

    if (!layer_info) return VK_ERROR_INITIALIZATION_FAILED;

    PFN_vkGetInstanceProcAddr next_gipa = layer_info->u.pLayerInfo->pfnNextGetInstanceProcAddr;
    PFN_vkGetDeviceProcAddr next_gdpa = layer_info->u.pLayerInfo->pfnNextGetDeviceProcAddr;
    PFN_vkCreateDevice next_create = reinterpret_cast<PFN_vkCreateDevice>(
        next_gipa(VK_NULL_HANDLE, "vkCreateDevice"));

    const_cast<VkLayerDeviceCreateInfo*>(layer_info)->u.pLayerInfo =
        layer_info->u.pLayerInfo->pNext;

    VkResult res = next_create(physicalDevice, pCreateInfo, pAllocator, pDevice);
    if (res == VK_SUCCESS && pDevice && *pDevice) {
        std::lock_guard<std::mutex> lock(g_mutex);
        void* key = get_dispatch_key(*pDevice);
        DeviceDispatch disp;
        disp.get_device_proc_addr = next_gdpa;
        disp.destroy_device = reinterpret_cast<PFN_vkDestroyDevice>(
            next_gdpa(*pDevice, "vkDestroyDevice"));
        disp.queue_present_khr = reinterpret_cast<PFN_vkQueuePresentKHR>(
            next_gdpa(*pDevice, "vkQueuePresentKHR"));
        g_device_dispatch[key] = disp;
    }
    return res;
}

static VKAPI_ATTR void VKAPI_CALL Phantom_vkDestroyDevice(
    VkDevice device,
    const VkAllocationCallbacks* pAllocator)
{
    void* key = get_dispatch_key(device);
    PFN_vkDestroyDevice next_destroy = nullptr;

    {
        std::lock_guard<std::mutex> lock(g_mutex);
        auto it = g_device_dispatch.find(key);
        if (it != g_device_dispatch.end()) {
            next_destroy = it->second.destroy_device;
            g_device_dispatch.erase(it);
        }
    }

    if (next_destroy) {
        next_destroy(device, pAllocator);
    }
}

extern "C" {

PHANTOM_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Phantom_vkGetInstanceProcAddr(
    VkInstance instance,
    const char* pName)
{
    if (std::strcmp(pName, "vkGetInstanceProcAddr") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkGetInstanceProcAddr);
    if (std::strcmp(pName, "vkCreateInstance") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkCreateInstance);
    if (std::strcmp(pName, "vkDestroyInstance") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkDestroyInstance);
    if (std::strcmp(pName, "vkCreateDevice") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkCreateDevice);

    if (instance == VK_NULL_HANDLE) return nullptr;

    void* key = get_dispatch_key(instance);
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_instance_dispatch.find(key);
    if (it != g_instance_dispatch.end() && it->second.get_instance_proc_addr) {
        return it->second.get_instance_proc_addr(instance, pName);
    }
    return nullptr;
}

PHANTOM_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Phantom_vkGetDeviceProcAddr(
    VkDevice device,
    const char* pName)
{
    if (std::strcmp(pName, "vkGetDeviceProcAddr") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkGetDeviceProcAddr);
    if (std::strcmp(pName, "vkDestroyDevice") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkDestroyDevice);
    if (std::strcmp(pName, "vkQueuePresentKHR") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkQueuePresentKHR);

    if (device == VK_NULL_HANDLE) return nullptr;

    void* key = get_dispatch_key(device);
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_device_dispatch.find(key);
    if (it != g_device_dispatch.end() && it->second.get_device_proc_addr) {
        return it->second.get_device_proc_addr(device, pName);
    }
    return nullptr;
}

PHANTOM_EXPORT VKAPI_ATTR VkResult VKAPI_CALL Phantom_vkNegotiateLoaderLayerInterfaceVersion(
    VkNegotiateLayerInterface* pVersionStruct)
{
    if (!pVersionStruct || pVersionStruct->sType != LAYER_NEGOTIATE_INTERFACE_STRUCT)
        return VK_ERROR_INITIALIZATION_FAILED;

    if (pVersionStruct->loaderLayerInterfaceVersion >= 2) {
        pVersionStruct->pfnGetInstanceProcAddr = Phantom_vkGetInstanceProcAddr;
        pVersionStruct->pfnGetDeviceProcAddr = Phantom_vkGetDeviceProcAddr;
        pVersionStruct->pfnGetPhysicalDeviceProcAddr = nullptr;
    }

    if (pVersionStruct->loaderLayerInterfaceVersion > 2) {
        pVersionStruct->loaderLayerInterfaceVersion = 2;
    }

    return VK_SUCCESS;
}

} // extern "C"
