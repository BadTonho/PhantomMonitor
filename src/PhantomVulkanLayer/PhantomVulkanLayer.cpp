#include "PhantomVulkanLayer.h"
#include <chrono>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <shared_mutex>
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

// Tabela de despacho para funÃ§Ãµes de instÃ¢ncia
struct InstanceDispatch {
    PFN_vkGetInstanceProcAddr get_instance_proc_addr = nullptr;
    PFN_vkDestroyInstance destroy_instance = nullptr;
};

// Tabela de despacho para funÃ§Ãµes de dispositivo
struct DeviceDispatch {
    PFN_vkGetDeviceProcAddr get_device_proc_addr = nullptr;
    PFN_vkDestroyDevice destroy_device = nullptr;
    PFN_vkQueuePresentKHR queue_present_khr = nullptr;
};

std::mutex g_mutex;
std::unordered_map<void*, InstanceDispatch> g_instance_dispatch;
std::unordered_map<void*, DeviceDispatch> g_device_dispatch;

// Telemetria e memÃ³ria compartilhada
std::atomic<PhantomVulkanTelemetryHeader*> g_telemetry_shm{nullptr};
int g_shm_fd = -1;
std::atomic<uint64_t> g_frame_counter{0};
std::atomic<uint64_t> g_last_present_ns{0};
// Nome do objeto de SHM criado por este processo (um por PID); usado pelo cleanup
// para remover a entrada de /dev/shm (ou o arquivo de /tmp) e evitar dados fantasmas.
std::string g_shm_path;
std::string g_shm_fallback_path;
// GIPA da camada seguinte (para repassar funÃ§Ãµes de physical device na interface v2).
std::atomic<PFN_vkGetInstanceProcAddr> g_next_gipa{nullptr};

uint64_t get_time_ns() {
    // Chrono steady: Ãºnica fonte nos dois SOs (no Windows o MSVC jÃ¡ usa QPC abaixo do
    // capÃ´) â€” antes havia divisÃ£o por zero se QueryPerformanceFrequency falhasse.
    return static_cast<uint64_t>(
        std::chrono::duration_cast<std::chrono::nanoseconds>(
            std::chrono::steady_clock::now().time_since_epoch()).count());
}

// Acesso Ã  regiÃ£o da SHM: presents tomam lock COMPARTILHADO antes de carregar o
// ponteiro e escrever; o cleanup toma o EXCLUSIVO antes do munmap. Sem isso, um
// present em voo podia escrever depois do munmap (write-after-munmap â†’ SIGSEGV
// dentro do processo do JOGO, exatamente no shutdown).
std::shared_mutex g_shm_access;

std::mutex g_shm_mutex;

void init_shared_memory() {
    std::lock_guard<std::mutex> lock(g_shm_mutex);
    if (g_telemetry_shm.load() != nullptr) return;

#if !defined(_WIN32)
    // 0600: apenas o mesmo usuÃ¡rio (jogo e PhantomMonitor) acessa a telemetria.
    // Um objeto de SHM POR PID: dois processos Vulkan simultÃ¢neos com a layer ativa
    // nÃ£o se pisotam mais (antes, o objeto Ãºnico era last-writer-wins e o FPS
    // saltava entre os jogos); o leitor C# enumera phantom_fps* e valida o PID.
    char shm_name[64];
    char fallback_name[80];
    const int pid = static_cast<int>(getpid());
    std::snprintf(shm_name, sizeof(shm_name), PHANTOM_SHM_PREFIX "%d", pid);
    std::snprintf(fallback_name, sizeof(fallback_name), PHANTOM_FALLBACK_PREFIX "%d.bin", pid);

    // O_CLOEXEC: o fd de telemetria nÃ£o Ã© herdado por processos filhos do jogo
    // (launchers, forks utilitÃ¡rios, Proton).
    g_shm_fd = shm_open(shm_name, O_CREAT | O_RDWR | O_CLOEXEC, 0600);
    if (g_shm_fd < 0) {
        // Fallback para arquivo comum em /tmp/phantom_fps_<pid>.bin
        g_shm_fd = open(fallback_name, O_CREAT | O_RDWR | O_CLOEXEC, 0600);
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
                shm->pid = static_cast<uint32_t>(pid);

                // Ler nome do processo em /proc/self/comm (com O_CLOEXEC tambÃ©m).
                if (int comm_fd = open("/proc/self/comm", O_RDONLY | O_CLOEXEC); comm_fd >= 0) {
                    char comm_buf[64] = {0};
                    ssize_t bytes_read = read(comm_fd, comm_buf, sizeof(comm_buf) - 1);
                    if (bytes_read > 0) {
                        if (comm_buf[bytes_read - 1] == '\n') comm_buf[bytes_read - 1] = '\0';
                        std::strncpy(shm->process_name, comm_buf, sizeof(shm->process_name) - 1);
                        shm->process_name[sizeof(shm->process_name) - 1] = '\0';
                    }
                    close(comm_fd);
                }

                g_shm_path = shm_name;
                g_shm_fallback_path = fallback_name;
                // Publica o ponteiro sÃ³ depois de inicializado.
                g_telemetry_shm.store(shm);
                return;
            }
        }
        // ftruncate/mmap falhou (ex.: /dev/shm cheia): fechar o fd â€” antes ele
        // vazava e uma nova tentativa de init reabria mais um.
        close(g_shm_fd);
        g_shm_fd = -1;
    }
#endif
}

void cleanup_shared_memory() {
    std::lock_guard<std::mutex> lock(g_shm_mutex);
#if !defined(_WIN32)
    // 1) Novos presents veem o ponteiro nulo e nÃ£o entram;
    // 2) Escritas em voo sÃ£o drenadas pelo lock exclusivo;
    // 3) SÃ³ entÃ£o munmap â€” sem write-after-munmap (crash do jogo).
    PhantomVulkanTelemetryHeader* shm = g_telemetry_shm.exchange(nullptr);
    {
        std::unique_lock<std::shared_mutex> access(g_shm_access);
    }
    if (shm != nullptr) {
        munmap(shm, sizeof(PhantomVulkanTelemetryHeader));
    }
    if (g_shm_fd >= 0) {
        close(g_shm_fd);
        g_shm_fd = -1;
    }

    // Remove as entradas do filesystem: sem isso /dev/shm (ou /tmp) acumulava o
    // objeto com dados congelados do processo morto â€” "FPS fantasma" no monitor.
    if (!g_shm_path.empty()) {
        shm_unlink(g_shm_path.c_str());
    }
    if (!g_shm_fallback_path.empty()) {
        unlink(g_shm_fallback_path.c_str());
    }
    g_shm_path.clear();
    g_shm_fallback_path.clear();

    // Ciclo createâ†’destroyâ†’create no mesmo processo reenicia do zero (antes a
    // primeira frame da nova sessÃ£o calculava frametime de minutos).
    g_frame_counter.store(0);
    g_last_present_ns.store(0);
#endif
}

// Chave do dispatch para ponteiros Vulkan (dispatchable handle).
// Null-check: apps bagaceiros chamam vkDestroyInstance(VK_NULL_HANDLE) â€” violaÃ§Ã£o de
// spec, mas desreferenciar um nullptr crashava o processo do jogo tambÃ©m.
void* get_dispatch_key(void* object) {
    return object ? *reinterpret_cast<void**>(object) : nullptr;
}

} // namespace

// InterceptaÃ§Ã£o de vkQueuePresentKHR: o coraÃ§Ã£o da mediÃ§Ã£o de quadros
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

    // exchange atÃ´mico: seguro com vÃ¡rios threads/filas chamando present ao mesmo tempo.
    uint64_t now_ns = get_time_ns();
    uint64_t prev_ns = g_last_present_ns.exchange(now_ns);
    if (prev_ns > 0 && now_ns > prev_ns) {
        uint64_t delta_ns = now_ns - prev_ns;
        float ft_ms = static_cast<float>(delta_ns) / 1000000.0f;
        float fps = (ft_ms > 0.001f) ? (1000.0f / ft_ms) : 0.0f;

        // Lock compartilhado ANTES do load + cleanup com lock exclusivo antes do
        // munmap: escrita nunca cai em regiÃ£o desmapeada (antes: SIGSEGV no jogo).
        std::shared_lock<std::shared_mutex> access(g_shm_access);
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
    // Sem cadeia para apresentar: retornar VK_SUCCESS mintava sucesso e a tela do
    // app congelava sem diagnÃ³stico nenhum. O erro denuncia a falha de despacho.
    return VK_ERROR_INITIALIZATION_FAILED;
}

static VKAPI_ATTR VkResult VKAPI_CALL Phantom_vkCreateInstance(
    const VkInstanceCreateInfo* pCreateInfo,
    const VkAllocationCallbacks* pAllocator,
    VkInstance* pInstance)
{
    if (pCreateInfo == nullptr) {
        // ViolaÃ§Ã£o de spec do app, mas desreferenciar nullptr crashava o processo.
        return VK_ERROR_INITIALIZATION_FAILED;
    }

    try
    {
        init_shared_memory();

        auto* layer_info = reinterpret_cast<const VkLayerInstanceCreateInfo*>(pCreateInfo->pNext);
        while (layer_info && (layer_info->sType != VK_STRUCTURE_TYPE_LOADER_INSTANCE_CREATE_INFO ||
                              layer_info->function != VK_LAYER_LINK_INFO)) {
            layer_info = reinterpret_cast<const VkLayerInstanceCreateInfo*>(layer_info->pNext);
        }

        // Caminhos de falha ANTES do destroy: sem estes checks a SHM ficava Ã³rfÃ£
        // (nunca passava pelo vkDestroyInstance) atÃ© o fim do processo.
        if (!layer_info || layer_info->u.pLayerInfo == nullptr ||
            layer_info->u.pLayerInfo->pfnNextGetInstanceProcAddr == nullptr) {
            cleanup_shared_memory();
            return VK_ERROR_INITIALIZATION_FAILED;
        }

        PFN_vkGetInstanceProcAddr next_gipa = layer_info->u.pLayerInfo->pfnNextGetInstanceProcAddr;
        // Guardado para o pfnGetPhysicalDeviceProcAddr da interface v2 (a layer nÃ£o
        // intercepta nada de physical device; apenas repassa para a cadeia).
        g_next_gipa.store(next_gipa);

        PFN_vkCreateInstance next_create = reinterpret_cast<PFN_vkCreateInstance>(
            next_gipa(VK_NULL_HANDLE, "vkCreateInstance"));
        if (next_create == nullptr) {
            cleanup_shared_memory();
            return VK_ERROR_INITIALIZATION_FAILED;
        }

        // AvanÃ§ar chain
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
    catch (const std::exception&)
    {
        // std::unordered_map/std::string podem lanÃ§ar bad_alloc; exceÃ§Ã£o atravessando
        // extern "C" seria terminate() do processo do jogo.
        cleanup_shared_memory();
        return VK_ERROR_OUT_OF_HOST_MEMORY;
    }
    catch (...)
    {
        cleanup_shared_memory();
        return VK_ERROR_OUT_OF_HOST_MEMORY;
    }
}

static VKAPI_ATTR void VKAPI_CALL Phantom_vkDestroyInstance(
    VkInstance instance,
    const VkAllocationCallbacks* pAllocator)
{
    if (instance == VK_NULL_HANDLE) return; // get_dispatch_key(nullptr) crashava

    try
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

        // Telemetria morre junto com a instÃ¢ncia do jogo (drain do lock antes do munmap).
        cleanup_shared_memory();

        if (next_destroy != nullptr) {
            next_destroy(instance, pAllocator);
        }
    }
    catch (...)
    {
        // Nunca deixe a exceÃ§Ã£o passar da fronteira extern "C" (terminaria o jogo);
        // garanta que a telemetria nÃ£o sobreviva ao destroy.
        cleanup_shared_memory();
    }
}

static VKAPI_ATTR VkResult VKAPI_CALL Phantom_vkCreateDevice(
    VkPhysicalDevice physicalDevice,
    const VkDeviceCreateInfo* pCreateInfo,
    const VkAllocationCallbacks* pAllocator,
    VkDevice* pDevice)
{
    if (pCreateInfo == nullptr) return VK_ERROR_INITIALIZATION_FAILED; // violaÃ§Ã£o de spec

    try
    {
        auto* layer_info = reinterpret_cast<const VkLayerDeviceCreateInfo*>(pCreateInfo->pNext);
        while (layer_info && (layer_info->sType != VK_STRUCTURE_TYPE_LOADER_DEVICE_CREATE_INFO ||
                              layer_info->function != VK_LAYER_LINK_INFO)) {
            layer_info = reinterpret_cast<const VkLayerDeviceCreateInfo*>(layer_info->pNext);
        }

        if (!layer_info || layer_info->u.pLayerInfo == nullptr ||
            layer_info->u.pLayerInfo->pfnNextGetInstanceProcAddr == nullptr) {
            return VK_ERROR_INITIALIZATION_FAILED;
        }

        PFN_vkGetInstanceProcAddr next_gipa = layer_info->u.pLayerInfo->pfnNextGetInstanceProcAddr;
        PFN_vkGetDeviceProcAddr next_gdpa = layer_info->u.pLayerInfo->pfnNextGetDeviceProcAddr;
        if (next_gdpa == nullptr) return VK_ERROR_INITIALIZATION_FAILED;
        PFN_vkCreateDevice next_create = reinterpret_cast<PFN_vkCreateDevice>(
            next_gipa(VK_NULL_HANDLE, "vkCreateDevice"));
        if (next_create == nullptr) return VK_ERROR_INITIALIZATION_FAILED;

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
    catch (const std::exception&)
    {
        // bad_alloc em unordered_map/std::string nÃ£o atravessa extern "C".
        return VK_ERROR_OUT_OF_HOST_MEMORY;
    }
    catch (...)
    {
        return VK_ERROR_OUT_OF_HOST_MEMORY;
    }
}

static VKAPI_ATTR void VKAPI_CALL Phantom_vkDestroyDevice(
    VkDevice device,
    const VkAllocationCallbacks* pAllocator)
{
    if (device == VK_NULL_HANDLE) return; // violaÃ§Ã£o de spec, mas nÃ£o crashar

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

    if (next_destroy != nullptr) {
        next_destroy(device, pAllocator);
    }
}

// Repasse para physical device (interface v2 do negotiate): a layer nÃ£o intercepta
// nenhuma funÃ§Ã£o de physical device â€” encaminha para o GIPA da cadeia.
static VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Phantom_vkGetPhysicalDeviceProcAddr(
    VkInstance instance,
    const char* pName)
{
    if (pName == nullptr) return nullptr;

    PFN_vkGetInstanceProcAddr next_gipa = g_next_gipa.load();
    if (next_gipa != nullptr) {
        return next_gipa(instance, pName);
    }
    return nullptr;
}

extern "C" {

PHANTOM_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Phantom_vkGetInstanceProcAddr(
    VkInstance instance,
    const char* pName)
{
    if (pName == nullptr) return nullptr;
    if (std::strcmp(pName, "vkGetInstanceProcAddr") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkGetInstanceProcAddr);
    if (std::strcmp(pName, "vkCreateInstance") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkCreateInstance);
    if (std::strcmp(pName, "vkDestroyInstance") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkDestroyInstance);
    if (std::strcmp(pName, "vkCreateDevice") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkCreateDevice);

    if (instance == VK_NULL_HANDLE) return nullptr;

    try
    {
        void* key = get_dispatch_key(instance);
        std::lock_guard<std::mutex> lock(g_mutex);
        auto it = g_instance_dispatch.find(key);
        if (it != g_instance_dispatch.end() && it->second.get_instance_proc_addr) {
            return it->second.get_instance_proc_addr(instance, pName);
        }
    }
    catch (...)
    {
        // fronteira extern "C": falha de alocaÃ§Ã£o no lookup nÃ£o termina o processo
    }
    return nullptr;
}

PHANTOM_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Phantom_vkGetDeviceProcAddr(
    VkDevice device,
    const char* pName)
{
    if (pName == nullptr) return nullptr;
    if (std::strcmp(pName, "vkGetDeviceProcAddr") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkGetDeviceProcAddr);
    if (std::strcmp(pName, "vkDestroyDevice") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkDestroyDevice);
    if (std::strcmp(pName, "vkQueuePresentKHR") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(Phantom_vkQueuePresentKHR);

    if (device == VK_NULL_HANDLE) return nullptr;

    try
    {
        void* key = get_dispatch_key(device);
        std::lock_guard<std::mutex> lock(g_mutex);
        auto it = g_device_dispatch.find(key);
        if (it != g_device_dispatch.end() && it->second.get_device_proc_addr) {
            return it->second.get_device_proc_addr(device, pName);
        }
    }
    catch (...)
    {
        // idem
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
        // A v2 da interface existe justamente para permitir este ponteiro; devolvÃª-lo
        // nulo deixava o comportamento dependente do fallback de loaders antigos.
        pVersionStruct->pfnGetPhysicalDeviceProcAddr = Phantom_vkGetPhysicalDeviceProcAddr;
    }

    if (pVersionStruct->loaderLayerInterfaceVersion > 2) {
        pVersionStruct->loaderLayerInterfaceVersion = 2;
    }

    return VK_SUCCESS;
}

} // extern "C"
