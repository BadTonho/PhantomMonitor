#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <d3d10.h>
#include <d3d9.h>
#include <dxgi1_4.h>
#include <GL/gl.h>

#include "OverlayFrameReader.h"
#include "OverlayImGuiDraw.h"
#include "OverlayStatus.h"
#include "MinHook.h"
#include "imgui.h"
#include "imgui_impl_dx11.h"
#include "imgui_impl_dx12.h"
#include "imgui_impl_dx10.h"
#include "imgui_impl_dx9.h"
#include "imgui_impl_opengl3.h"

#include <algorithm>
#include <cstdint>
#include <exception>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace {
using namespace phantom::overlay;

using PresentFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
using ResizeBuffersFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT);
using D3D9PresentFn = HRESULT(STDMETHODCALLTYPE*)(IDirect3DDevice9*, const RECT*, const RECT*, HWND, const RGNDATA*);
using D3D9ResetFn = HRESULT(STDMETHODCALLTYPE*)(IDirect3DDevice9*, D3DPRESENT_PARAMETERS*);
using GlSwapBuffersFn = BOOL(WINAPI*)(HDC);
using ReleaseFn = ULONG(STDMETHODCALLTYPE*)(IUnknown*);
using CreateSwapChainFn = HRESULT(STDMETHODCALLTYPE*)(IDXGIFactory*, IUnknown*, DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**);
using CreateSwapChainForHwndFn = HRESULT(STDMETHODCALLTYPE*)(IDXGIFactory2*, IUnknown*, HWND,
    const DXGI_SWAP_CHAIN_DESC1*, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC*, IDXGIOutput*, IDXGISwapChain1**);

PresentFn g_present = nullptr;
ResizeBuffersFn g_resize_buffers = nullptr;
D3D9PresentFn g_d3d9_present = nullptr;
D3D9ResetFn g_d3d9_reset = nullptr;
GlSwapBuffersFn g_gl_swap_buffers = nullptr;
ReleaseFn g_release = nullptr;
CreateSwapChainFn g_create_swap_chain = nullptr;
CreateSwapChainForHwndFn g_create_swap_chain_for_hwnd = nullptr;
std::mutex g_render_mutex;
std::mutex g_swapchain_mutex;
IDXGISwapChain* g_active_swapchain = nullptr;
std::unordered_map<IDXGISwapChain*, ID3D12CommandQueue*> g_queues;
FrameReader g_reader;
StatusPublisher g_status(StatusProducer::WindowsHook);
ImGuiContext* g_imgui = nullptr;
bool g_imgui_backend_initialized = false;
RenderApi g_api = RenderApi::Unknown;

ID3D11Device* g_d3d11_device = nullptr;
ID3D11DeviceContext* g_d3d11_context = nullptr;
ID3D11RenderTargetView* g_d3d11_target = nullptr;
ID3D10Device* g_d3d10_device = nullptr;
ID3D10RenderTargetView* g_d3d10_target = nullptr;
IDirect3DDevice9* g_d3d9_device = nullptr;
HGLRC g_gl_context = nullptr;
bool g_gl_initialized = false;

struct D3D12Frame {
    ID3D12CommandAllocator* allocator = nullptr;
    ID3D12Resource* target = nullptr;
    std::uint64_t fence_value = 0;
};
ID3D12Device* g_d3d12_device = nullptr;
ID3D12CommandQueue* g_d3d12_queue = nullptr;
ID3D12GraphicsCommandList* g_d3d12_commands = nullptr;
ID3D12DescriptorHeap* g_d3d12_rtv_heap = nullptr;
ID3D12DescriptorHeap* g_d3d12_srv_heap = nullptr;
ID3D12Fence* g_d3d12_fence = nullptr;
HANDLE g_d3d12_fence_event = nullptr;
UINT g_d3d12_rtv_increment = 0;
UINT g_d3d12_frame_index = 0;
std::uint64_t g_d3d12_next_fence = 1;
std::vector<D3D12Frame> g_d3d12_frames;
DXGI_FORMAT g_d3d12_format = DXGI_FORMAT_UNKNOWN;

class InitializationSignal {
public:
    InitializationSignal() {
        const auto name = L"Local\\PhantomMonitor.InGameOverlay.HookReady." + std::to_wstring(GetCurrentProcessId());
        event_ = CreateEventW(nullptr, TRUE, FALSE, name.c_str());
    }
    ~InitializationSignal() {
        if (event_) {
            SetEvent(event_);
            CloseHandle(event_);
        }
    }
private:
    HANDLE event_ = nullptr;
};

void set_status(RenderApi api, RenderState state, const std::string& message) noexcept {
    try { g_status.Update(api, state, message); }
    catch (...) { }
}

void release_d3d11_target() {
    if (g_d3d11_target) { g_d3d11_target->Release(); g_d3d11_target = nullptr; }
}

void release_d3d10_target() {
    if (g_d3d10_target) { g_d3d10_target->Release(); g_d3d10_target = nullptr; }
}

void shutdown_imgui() {
    if (g_imgui) {
        ImGui::SetCurrentContext(g_imgui);
        if (g_imgui_backend_initialized) {
            if (g_api == RenderApi::Direct3D11) ImGui_ImplDX11_Shutdown();
            else if (g_api == RenderApi::Direct3D12) ImGui_ImplDX12_Shutdown();
            else if (g_api == RenderApi::Direct3D10) ImGui_ImplDX10_Shutdown();
            else if (g_api == RenderApi::Direct3D9) ImGui_ImplDX9_Shutdown();
            else if (g_api == RenderApi::OpenGL && wglGetCurrentContext() == g_gl_context)
                ImGui_ImplOpenGL3_Shutdown();
        }
        ImGui::DestroyContext(g_imgui);
        g_imgui = nullptr;
    }
    g_imgui_backend_initialized = false;
    g_api = RenderApi::Unknown;
}

void wait_d3d12_queue() {
    if (!g_d3d12_queue || !g_d3d12_fence || !g_d3d12_fence_event) return;
    const auto value = g_d3d12_next_fence++;
    if (SUCCEEDED(g_d3d12_queue->Signal(g_d3d12_fence, value))
        && g_d3d12_fence->GetCompletedValue() < value) {
        if (SUCCEEDED(g_d3d12_fence->SetEventOnCompletion(value, g_d3d12_fence_event)))
            WaitForSingleObject(g_d3d12_fence_event, 5000);
    }
}

void release_d3d12_resources(bool release_device) {
    wait_d3d12_queue();
    shutdown_imgui();
    for (auto& frame : g_d3d12_frames) {
        if (frame.target) frame.target->Release();
        if (frame.allocator) frame.allocator->Release();
    }
    g_d3d12_frames.clear();
    if (g_d3d12_commands) { g_d3d12_commands->Release(); g_d3d12_commands = nullptr; }
    if (g_d3d12_rtv_heap) { g_d3d12_rtv_heap->Release(); g_d3d12_rtv_heap = nullptr; }
    if (g_d3d12_srv_heap) { g_d3d12_srv_heap->Release(); g_d3d12_srv_heap = nullptr; }
    if (g_d3d12_fence) { g_d3d12_fence->Release(); g_d3d12_fence = nullptr; }
    if (g_d3d12_fence_event) { CloseHandle(g_d3d12_fence_event); g_d3d12_fence_event = nullptr; }
    if (release_device) {
        if (g_d3d12_queue) { g_d3d12_queue->Release(); g_d3d12_queue = nullptr; }
        if (g_d3d12_device) { g_d3d12_device->Release(); g_d3d12_device = nullptr; }
    }
}

void reset_renderer_devices() {
    release_d3d12_resources(true);
    release_d3d11_target();
    if (g_d3d11_context) { g_d3d11_context->Release(); g_d3d11_context = nullptr; }
    if (g_d3d11_device) { g_d3d11_device->Release(); g_d3d11_device = nullptr; }
    release_d3d10_target();
    if (g_d3d10_device) { g_d3d10_device->Release(); g_d3d10_device = nullptr; }
    if (g_d3d9_device) { g_d3d9_device->Release(); g_d3d9_device = nullptr; }
    g_gl_context = nullptr;
    g_gl_initialized = false;
}

bool read_frame(Frame& frame, RenderApi api);

bool ensure_d3d11(IDXGISwapChain* swapchain) {
    ID3D11Device* device = nullptr;
    if (FAILED(swapchain->GetDevice(__uuidof(ID3D11Device), reinterpret_cast<void**>(&device)))) {
        set_status(RenderApi::Direct3D11, RenderState::Failed, "Não foi possível obter o dispositivo Direct3D 11 da swapchain.");
        return false;
    }
    if (g_d3d11_device != device || g_api != RenderApi::Direct3D11) {
        reset_renderer_devices();
        g_d3d11_device = device;
        g_d3d11_device->GetImmediateContext(&g_d3d11_context);
        g_imgui = ImGui::CreateContext();
        if (!g_imgui || !ImGui_ImplDX11_Init(g_d3d11_device, g_d3d11_context)) {
            shutdown_imgui();
            set_status(RenderApi::Direct3D11, RenderState::Failed, "Dear ImGui não inicializou no Direct3D 11.");
            return false;
        }
        g_imgui_backend_initialized = true;
        g_api = RenderApi::Direct3D11;
    } else {
        device->Release();
    }
    if (g_d3d11_target) return true;
    ID3D11Texture2D* back_buffer = nullptr;
    if (FAILED(swapchain->GetBuffer(0, __uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&back_buffer)))) {
        set_status(RenderApi::Direct3D11, RenderState::Failed, "Não foi possível obter o buffer da swapchain Direct3D 11.");
        return false;
    }
    const auto result = g_d3d11_device->CreateRenderTargetView(back_buffer, nullptr, &g_d3d11_target);
    back_buffer->Release();
    if (FAILED(result) || !g_d3d11_target) {
        set_status(RenderApi::Direct3D11, RenderState::Failed, "CreateRenderTargetView falhou no Direct3D 11.");
        return false;
    }
    return true;
}

bool ensure_d3d10(IDXGISwapChain* swapchain) {
    ID3D10Device* device = nullptr;
    if (FAILED(swapchain->GetDevice(__uuidof(ID3D10Device), reinterpret_cast<void**>(&device)))) {
        set_status(RenderApi::Direct3D10, RenderState::Failed, "Não foi possível obter o dispositivo Direct3D 10 da swapchain.");
        return false;
    }
    if (g_d3d10_device != device || g_api != RenderApi::Direct3D10) {
        reset_renderer_devices();
        g_d3d10_device = device;
        g_imgui = ImGui::CreateContext();
        g_api = RenderApi::Direct3D10;
        if (!g_imgui || !ImGui_ImplDX10_Init(g_d3d10_device)) {
            shutdown_imgui();
            set_status(RenderApi::Direct3D10, RenderState::Failed, "Dear ImGui não inicializou no Direct3D 10.");
            return false;
        }
        g_imgui_backend_initialized = true;
    } else {
        device->Release();
    }
    if (g_d3d10_target) return true;
    ID3D10Texture2D* back_buffer = nullptr;
    if (FAILED(swapchain->GetBuffer(0, __uuidof(ID3D10Texture2D), reinterpret_cast<void**>(&back_buffer)))) {
        set_status(RenderApi::Direct3D10, RenderState::Failed, "Não foi possível obter o buffer da swapchain Direct3D 10.");
        return false;
    }
    const auto result = g_d3d10_device->CreateRenderTargetView(back_buffer, nullptr, &g_d3d10_target);
    back_buffer->Release();
    if (FAILED(result) || !g_d3d10_target) {
        set_status(RenderApi::Direct3D10, RenderState::Failed, "CreateRenderTargetView falhou no Direct3D 10.");
        return false;
    }
    return true;
}

bool ensure_d3d9(IDirect3DDevice9* device) {
    if (g_d3d9_device != device || g_api != RenderApi::Direct3D9) {
        reset_renderer_devices();
        device->AddRef();
        g_d3d9_device = device;
        g_imgui = ImGui::CreateContext();
        g_api = RenderApi::Direct3D9;
        if (!g_imgui || !ImGui_ImplDX9_Init(g_d3d9_device)) {
            shutdown_imgui();
            set_status(RenderApi::Direct3D9, RenderState::Failed, "Dear ImGui não inicializou no Direct3D 9.");
            return false;
        }
        g_imgui_backend_initialized = true;
    }
    return true;
}

bool ensure_opengl(HGLRC current) {
    if (!current) return false;
    if (g_gl_context && g_gl_context != current) {
        set_status(RenderApi::OpenGL, RenderState::Unsupported,
            "Mais de um contexto WGL foi apresentado; o renderer mantém o primeiro contexto.");
        return false;
    }
    if (g_gl_initialized) return true;
    reset_renderer_devices();
    g_gl_context = current;
    g_imgui = ImGui::CreateContext();
    g_api = RenderApi::OpenGL;
    if (!g_imgui || !ImGui_ImplOpenGL3_Init("#version 130")) {
        shutdown_imgui();
        set_status(RenderApi::OpenGL, RenderState::Failed, "Dear ImGui não inicializou o backend OpenGL3/WGL.");
        return false;
    }
    g_imgui_backend_initialized = true;
    g_gl_initialized = true;
    return true;
}

void render_d3d10(IDXGISwapChain* swapchain) {
    Frame frame;
    if (!read_frame(frame, RenderApi::Direct3D10) || !ensure_d3d10(swapchain)) return;
    DXGI_SWAP_CHAIN_DESC desc{};
    if (FAILED(swapchain->GetDesc(&desc)) || desc.BufferDesc.Width == 0 || desc.BufferDesc.Height == 0) return;
    ImGui::SetCurrentContext(g_imgui);
    ImGui_ImplDX10_NewFrame();
    ImDrawData* draw_data = nullptr;
    DrawFrame(frame, desc.BufferDesc.Width, desc.BufferDesc.Height,
        static_cast<float>(desc.BufferDesc.Width) / frame.reference_width,
        static_cast<float>(desc.BufferDesc.Height) / frame.reference_height, &draw_data);
    if (!draw_data) return;
    ID3D10RenderTargetView* old_target = nullptr;
    ID3D10DepthStencilView* old_depth = nullptr;
    g_d3d10_device->OMGetRenderTargets(1, &old_target, &old_depth);
    g_d3d10_device->OMSetRenderTargets(1, &g_d3d10_target, nullptr);
    ImGui_ImplDX10_RenderDrawData(draw_data);
    g_d3d10_device->OMSetRenderTargets(1, &old_target, old_depth);
    if (old_target) old_target->Release();
    if (old_depth) old_depth->Release();
    set_status(RenderApi::Direct3D10, RenderState::Active, "Renderer Direct3D 10 desenhando o quadro aplicado.");
}

void render_d3d9(IDirect3DDevice9* device) {
    Frame frame;
    if (!read_frame(frame, RenderApi::Direct3D9) || !ensure_d3d9(device)) return;
    D3DDEVICE_CREATION_PARAMETERS creation{};
    if (FAILED(device->GetCreationParameters(&creation))) return;
    RECT client{};
    if (!creation.hFocusWindow || !GetClientRect(creation.hFocusWindow, &client)) return;
    const auto width = static_cast<std::uint32_t>(std::max<LONG>(0, client.right - client.left));
    const auto height = static_cast<std::uint32_t>(std::max<LONG>(0, client.bottom - client.top));
    if (width == 0 || height == 0) return;
    ImGui::SetCurrentContext(g_imgui);
    ImGui_ImplDX9_NewFrame();
    ImDrawData* draw_data = nullptr;
    DrawFrame(frame, width, height, static_cast<float>(width) / frame.reference_width,
        static_cast<float>(height) / frame.reference_height, &draw_data);
    if (!draw_data) return;
    ImGui_ImplDX9_RenderDrawData(draw_data);
    set_status(RenderApi::Direct3D9, RenderState::Active, "Renderer Direct3D 9 desenhando o quadro aplicado.");
}

void render_opengl(HDC dc) {
    const auto context = wglGetCurrentContext();
    if (!ensure_opengl(context)) return;
    Frame frame;
    if (!read_frame(frame, RenderApi::OpenGL)) return;
    RECT client{};
    const auto window = WindowFromDC(dc);
    if (!window || !GetClientRect(window, &client)) return;
    const auto width = static_cast<std::uint32_t>(std::max<LONG>(0, client.right - client.left));
    const auto height = static_cast<std::uint32_t>(std::max<LONG>(0, client.bottom - client.top));
    if (width == 0 || height == 0) return;
    ImGui::SetCurrentContext(g_imgui);
    ImGui_ImplOpenGL3_NewFrame();
    ImDrawData* draw_data = nullptr;
    DrawFrame(frame, width, height, static_cast<float>(width) / frame.reference_width,
        static_cast<float>(height) / frame.reference_height, &draw_data);
    if (draw_data) {
        ImGui_ImplOpenGL3_RenderDrawData(draw_data);
        set_status(RenderApi::OpenGL, RenderState::Active, "Renderer OpenGL/WGL desenhando o quadro aplicado.");
    }
}

HRESULT STDMETHODCALLTYPE detour_d3d9_present(IDirect3DDevice9* device, const RECT* source,
    const RECT* destination, HWND override_window, const RGNDATA* dirty_region) {
    try {
        std::lock_guard<std::mutex> lock(g_render_mutex);
        render_d3d9(device);
    } catch (const std::exception& error) {
        set_status(RenderApi::Direct3D9, RenderState::Failed, std::string("Exceção no renderer D3D9: ") + error.what());
    } catch (...) {
        set_status(RenderApi::Direct3D9, RenderState::Failed, "Exceção desconhecida no renderer D3D9.");
    }
    return g_d3d9_present(device, source, destination, override_window, dirty_region);
}

HRESULT STDMETHODCALLTYPE detour_d3d9_reset(IDirect3DDevice9* device,
    D3DPRESENT_PARAMETERS* parameters) {
    std::lock_guard<std::mutex> lock(g_render_mutex);
    if (device == g_d3d9_device && g_api == RenderApi::Direct3D9 && g_imgui) {
        ImGui::SetCurrentContext(g_imgui);
        ImGui_ImplDX9_InvalidateDeviceObjects();
    }
    const auto result = g_d3d9_reset(device, parameters);
    if (SUCCEEDED(result) && device == g_d3d9_device && g_api == RenderApi::Direct3D9 && g_imgui) {
        ImGui::SetCurrentContext(g_imgui);
        if (!ImGui_ImplDX9_CreateDeviceObjects())
            set_status(RenderApi::Direct3D9, RenderState::Failed, "O Direct3D 9 não recriou os recursos do overlay após Reset.");
    } else if (FAILED(result)) {
        set_status(RenderApi::Direct3D9, RenderState::Failed,
            "IDirect3DDevice9::Reset falhou: " + std::to_string(static_cast<unsigned long>(result)));
    }
    return result;
}

BOOL WINAPI detour_gl_swap_buffers(HDC dc) {
    if (wglGetCurrentContext() != nullptr) {
        try {
            std::lock_guard<std::mutex> lock(g_render_mutex);
            render_opengl(dc);
        } catch (const std::exception& error) {
            set_status(RenderApi::OpenGL, RenderState::Failed, std::string("Exceção no renderer OpenGL: ") + error.what());
        } catch (...) {
            set_status(RenderApi::OpenGL, RenderState::Failed, "Exceção desconhecida no renderer OpenGL.");
        }
    }
    return g_gl_swap_buffers(dc);
}

bool ensure_d3d12(IDXGISwapChain* swapchain, ID3D12CommandQueue* queue) {
    if (!queue) {
        set_status(RenderApi::Direct3D12, RenderState::Unsupported,
            "Swapchain Direct3D 12 sem a fila capturada na criação; reinicie o jogo após iniciar pelo PhantomMonitor.");
        return false;
    }
    ID3D12Device* device = nullptr;
    if (FAILED(swapchain->GetDevice(__uuidof(ID3D12Device), reinterpret_cast<void**>(&device)))) {
        set_status(RenderApi::Direct3D12, RenderState::Failed, "Não foi possível obter o dispositivo Direct3D 12 da swapchain.");
        return false;
    }
    DXGI_SWAP_CHAIN_DESC desc{};
    if (FAILED(swapchain->GetDesc(&desc))) {
        device->Release();
        set_status(RenderApi::Direct3D12, RenderState::Failed, "IDXGISwapChain::GetDesc falhou no Direct3D 12.");
        return false;
    }
    if (g_d3d12_device != device || g_d3d12_queue != queue || g_api != RenderApi::Direct3D12
        || g_d3d12_frames.size() != desc.BufferCount || g_d3d12_format != desc.BufferDesc.Format) {
        reset_renderer_devices();
        g_d3d12_device = device;
        g_d3d12_queue = queue;
        g_d3d12_device->AddRef();
        g_d3d12_queue->AddRef();
        g_d3d12_format = desc.BufferDesc.Format;
        D3D12_COMMAND_QUEUE_DESC queue_desc = queue->GetDesc();
        if (queue_desc.Type != D3D12_COMMAND_LIST_TYPE_DIRECT) {
            set_status(RenderApi::Direct3D12, RenderState::Unsupported, "A fila Direct3D 12 não é DIRECT.");
            return false;
        }
        D3D12_DESCRIPTOR_HEAP_DESC rtv_desc{};
        rtv_desc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        rtv_desc.NumDescriptors = desc.BufferCount;
        auto result = g_d3d12_device->CreateDescriptorHeap(&rtv_desc, IID_PPV_ARGS(&g_d3d12_rtv_heap));
        if (FAILED(result)) {
            set_status(RenderApi::Direct3D12, RenderState::Failed, "Criação do heap RTV falhou: " + std::to_string(static_cast<unsigned long>(result)));
            return false;
        }
        rtv_desc = {};
        rtv_desc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        rtv_desc.NumDescriptors = 1;
        rtv_desc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        result = g_d3d12_device->CreateDescriptorHeap(&rtv_desc, IID_PPV_ARGS(&g_d3d12_srv_heap));
        if (FAILED(result)) {
            set_status(RenderApi::Direct3D12, RenderState::Failed, "Criação do heap de shaders falhou: " + std::to_string(static_cast<unsigned long>(result)));
            return false;
        }
        g_d3d12_rtv_increment = g_d3d12_device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        g_d3d12_frames.resize(desc.BufferCount);
        for (UINT index = 0; index < desc.BufferCount; ++index) {
            auto& frame = g_d3d12_frames[index];
            result = g_d3d12_device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,
                IID_PPV_ARGS(&frame.allocator));
            if (FAILED(result)) {
                set_status(RenderApi::Direct3D12, RenderState::Failed, "Criação do command allocator falhou: " + std::to_string(static_cast<unsigned long>(result)));
                return false;
            }
            result = swapchain->GetBuffer(index, IID_PPV_ARGS(&frame.target));
            if (FAILED(result)) {
                set_status(RenderApi::Direct3D12, RenderState::Failed, "Obtenção do buffer D3D12 falhou: " + std::to_string(static_cast<unsigned long>(result)));
                return false;
            }
            auto handle = g_d3d12_rtv_heap->GetCPUDescriptorHandleForHeapStart();
            handle.ptr += static_cast<SIZE_T>(index) * g_d3d12_rtv_increment;
            g_d3d12_device->CreateRenderTargetView(frame.target, nullptr, handle);
        }
        result = g_d3d12_device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT,
            g_d3d12_frames[0].allocator, nullptr, IID_PPV_ARGS(&g_d3d12_commands));
        if (FAILED(result)) {
            set_status(RenderApi::Direct3D12, RenderState::Failed, "Criação da command list falhou: " + std::to_string(static_cast<unsigned long>(result)));
            return false;
        }
        result = g_d3d12_commands->Close();
        if (FAILED(result)) {
            set_status(RenderApi::Direct3D12, RenderState::Failed, "Fechamento inicial da command list falhou.");
            return false;
        }
        result = g_d3d12_device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&g_d3d12_fence));
        if (FAILED(result)) {
            set_status(RenderApi::Direct3D12, RenderState::Failed, "Criação da fence falhou: " + std::to_string(static_cast<unsigned long>(result)));
            return false;
        }
        g_d3d12_fence_event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!g_d3d12_fence_event) {
            set_status(RenderApi::Direct3D12, RenderState::Failed, "CreateEvent falhou para sincronização do renderer D3D12.");
            return false;
        }
        g_imgui = ImGui::CreateContext();
        if (!g_imgui) {
            set_status(RenderApi::Direct3D12, RenderState::Failed, "Não foi possível criar o contexto Dear ImGui para D3D12.");
            return false;
        }
        ImGui_ImplDX12_InitInfo init{};
        init.Device = g_d3d12_device;
        init.CommandQueue = g_d3d12_queue;
        init.NumFramesInFlight = static_cast<int>(desc.BufferCount);
        init.RTVFormat = desc.BufferDesc.Format;
        init.SrvDescriptorHeap = g_d3d12_srv_heap;
        init.LegacySingleSrvCpuDescriptor = g_d3d12_srv_heap->GetCPUDescriptorHandleForHeapStart();
        init.LegacySingleSrvGpuDescriptor = g_d3d12_srv_heap->GetGPUDescriptorHandleForHeapStart();
        g_api = RenderApi::Direct3D12;
        if (!ImGui_ImplDX12_Init(&init)) {
            shutdown_imgui();
            set_status(RenderApi::Direct3D12, RenderState::Failed, "Dear ImGui não inicializou no Direct3D 12.");
            return false;
        }
        g_imgui_backend_initialized = true;
    } else {
        device->Release();
    }
    return true;
}

bool read_frame(Frame& frame, RenderApi api) {
    std::string error;
    if (!g_reader.TryRead(frame, error)) {
        const auto stale = error == "frame is stale";
        set_status(api, stale ? RenderState::Stale : RenderState::Waiting,
            stale ? "Quadro aplicado expirou; nada foi desenhado." : "Aguardando quadro aplicado válido: " + error);
        return false;
    }
    if (!frame.visible) {
        set_status(api, RenderState::Hidden, "Overlay dentro do jogo oculto pelo PhantomMonitor.");
        return false;
    }
    return true;
}

void render_d3d11(IDXGISwapChain* swapchain) {
    Frame frame;
    if (!read_frame(frame, RenderApi::Direct3D11) || !ensure_d3d11(swapchain)) return;
    DXGI_SWAP_CHAIN_DESC desc{};
    if (FAILED(swapchain->GetDesc(&desc)) || desc.BufferDesc.Width == 0 || desc.BufferDesc.Height == 0) return;
    ImGui::SetCurrentContext(g_imgui);
    ImGui_ImplDX11_NewFrame();
    ImDrawData* draw_data = nullptr;
    DrawFrame(frame, desc.BufferDesc.Width, desc.BufferDesc.Height,
        static_cast<float>(desc.BufferDesc.Width) / frame.reference_width,
        static_cast<float>(desc.BufferDesc.Height) / frame.reference_height, &draw_data);
    if (!draw_data) return;
    ID3D11RenderTargetView* old_target = nullptr;
    g_d3d11_context->OMGetRenderTargets(1, &old_target, nullptr);
    g_d3d11_context->OMSetRenderTargets(1, &g_d3d11_target, nullptr);
    ImGui_ImplDX11_RenderDrawData(draw_data);
    g_d3d11_context->OMSetRenderTargets(1, &old_target, nullptr);
    if (old_target) old_target->Release();
    set_status(RenderApi::Direct3D11, RenderState::Active, "Renderer Direct3D 11 desenhando o quadro aplicado.");
}

bool wait_d3d12_frame(D3D12Frame& frame) {
    if (frame.fence_value == 0 || g_d3d12_fence->GetCompletedValue() >= frame.fence_value) return true;
    if (FAILED(g_d3d12_fence->SetEventOnCompletion(frame.fence_value, g_d3d12_fence_event))) return false;
    return WaitForSingleObject(g_d3d12_fence_event, 5000) == WAIT_OBJECT_0;
}

void render_d3d12(IDXGISwapChain* swapchain, ID3D12CommandQueue* queue) {
    Frame frame;
    if (!read_frame(frame, RenderApi::Direct3D12) || !ensure_d3d12(swapchain, queue)) return;
    IDXGISwapChain3* swapchain3 = nullptr;
    if (FAILED(swapchain->QueryInterface(__uuidof(IDXGISwapChain3), reinterpret_cast<void**>(&swapchain3)))) {
        set_status(RenderApi::Direct3D12, RenderState::Unsupported,
            "A swapchain D3D12 não oferece IDXGISwapChain3 para identificar o buffer apresentado.");
        return;
    }
    const auto index = swapchain3->GetCurrentBackBufferIndex();
    swapchain3->Release();
    if (index >= g_d3d12_frames.size()) return;
    auto& context = g_d3d12_frames[index];
    if (!wait_d3d12_frame(context)) {
        set_status(RenderApi::Direct3D12, RenderState::Failed, "Timeout esperando o frame Direct3D 12.");
        return;
    }
    if (FAILED(context.allocator->Reset()) || FAILED(g_d3d12_commands->Reset(context.allocator, nullptr))) {
        set_status(RenderApi::Direct3D12, RenderState::Failed, "Não foi possível reiniciar os comandos Direct3D 12.");
        return;
    }
    DXGI_SWAP_CHAIN_DESC desc{};
    if (FAILED(swapchain->GetDesc(&desc)) || desc.BufferDesc.Width == 0 || desc.BufferDesc.Height == 0) return;
    ImGui::SetCurrentContext(g_imgui);
    ImGui_ImplDX12_SetFrameIndex(index);
    ImGui_ImplDX12_NewFrame();
    ImDrawData* draw_data = nullptr;
    DrawFrame(frame, desc.BufferDesc.Width, desc.BufferDesc.Height,
        static_cast<float>(desc.BufferDesc.Width) / frame.reference_width,
        static_cast<float>(desc.BufferDesc.Height) / frame.reference_height, &draw_data);
    if (!draw_data) return;
    D3D12_RESOURCE_BARRIER barrier{};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = context.target;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_PRESENT;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_RENDER_TARGET;
    g_d3d12_commands->ResourceBarrier(1, &barrier);
    auto rtv = g_d3d12_rtv_heap->GetCPUDescriptorHandleForHeapStart();
    rtv.ptr += static_cast<SIZE_T>(index) * g_d3d12_rtv_increment;
    g_d3d12_commands->OMSetRenderTargets(1, &rtv, FALSE, nullptr);
    ID3D12DescriptorHeap* heaps[] = { g_d3d12_srv_heap };
    g_d3d12_commands->SetDescriptorHeaps(1, heaps);
    ImGui_ImplDX12_RenderDrawData(draw_data, g_d3d12_commands);
    std::swap(barrier.Transition.StateBefore, barrier.Transition.StateAfter);
    g_d3d12_commands->ResourceBarrier(1, &barrier);
    if (FAILED(g_d3d12_commands->Close())) return;
    ID3D12CommandList* lists[] = { g_d3d12_commands };
    g_d3d12_queue->ExecuteCommandLists(1, lists);
    const auto fence_value = g_d3d12_next_fence++;
    if (FAILED(g_d3d12_queue->Signal(g_d3d12_fence, fence_value))) return;
    context.fence_value = fence_value;
    set_status(RenderApi::Direct3D12, RenderState::Active, "Renderer Direct3D 12 desenhando o quadro aplicado.");
}

ID3D12CommandQueue* find_queue(IDXGISwapChain* swapchain) {
    std::lock_guard<std::mutex> lock(g_swapchain_mutex);
    const auto found = g_queues.find(swapchain);
    if (found == g_queues.end()) return nullptr;
    found->second->AddRef();
    return found->second;
}

void observe_swapchain(IDXGISwapChain* swapchain, IUnknown* device_or_queue) {
    if (!swapchain || !device_or_queue) return;
    ID3D12CommandQueue* queue = nullptr;
    if (SUCCEEDED(device_or_queue->QueryInterface(__uuidof(ID3D12CommandQueue), reinterpret_cast<void**>(&queue)))) {
        std::lock_guard<std::mutex> lock(g_swapchain_mutex);
        auto& stored = g_queues[swapchain];
        if (stored) stored->Release();
        stored = queue;
    }
}

HRESULT STDMETHODCALLTYPE detour_create_swap_chain(IDXGIFactory* factory, IUnknown* device,
    DXGI_SWAP_CHAIN_DESC* desc, IDXGISwapChain** swapchain) {
    const auto result = g_create_swap_chain(factory, device, desc, swapchain);
    if (SUCCEEDED(result) && swapchain && *swapchain) observe_swapchain(*swapchain, device);
    return result;
}

HRESULT STDMETHODCALLTYPE detour_create_swap_chain_for_hwnd(IDXGIFactory2* factory, IUnknown* device,
    HWND hwnd, const DXGI_SWAP_CHAIN_DESC1* desc, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* fullscreen,
    IDXGIOutput* output, IDXGISwapChain1** swapchain) {
    const auto result = g_create_swap_chain_for_hwnd(factory, device, hwnd, desc, fullscreen, output, swapchain);
    if (SUCCEEDED(result) && swapchain && *swapchain) observe_swapchain(*swapchain, device);
    return result;
}

HRESULT STDMETHODCALLTYPE detour_resize_buffers(IDXGISwapChain* swapchain, UINT count,
    UINT width, UINT height, DXGI_FORMAT format, UINT flags) {
    std::lock_guard<std::mutex> lock(g_render_mutex);
    if (swapchain == g_active_swapchain && g_api == RenderApi::Direct3D11) {
        release_d3d11_target();
        shutdown_imgui();
        if (g_d3d11_context) { g_d3d11_context->Release(); g_d3d11_context = nullptr; }
        if (g_d3d11_device) { g_d3d11_device->Release(); g_d3d11_device = nullptr; }
    } else if (swapchain == g_active_swapchain && g_api == RenderApi::Direct3D12) {
        release_d3d12_resources(false);
    } else if (swapchain == g_active_swapchain && g_api == RenderApi::Direct3D10) {
        release_d3d10_target();
        shutdown_imgui();
        if (g_d3d10_device) { g_d3d10_device->Release(); g_d3d10_device = nullptr; }
    }
    const auto result = g_resize_buffers(swapchain, count, width, height, format, flags);
    if (FAILED(result)) set_status(g_api, RenderState::Failed,
        "IDXGISwapChain::ResizeBuffers falhou: " + std::to_string(static_cast<unsigned long>(result)));
    return result;
}

HRESULT STDMETHODCALLTYPE detour_present(IDXGISwapChain* swapchain, UINT sync, UINT flags) {
    if ((flags & DXGI_PRESENT_TEST) == 0) {
      try {
        std::lock_guard<std::mutex> lock(g_render_mutex);
        if (!g_active_swapchain) g_active_swapchain = swapchain;
        if (g_active_swapchain != swapchain) {
            set_status(RenderApi::Unknown, RenderState::Unsupported,
                "Mais de uma swapchain DXGI foi detectada; o renderer mantém a primeira para evitar desenhar no jogo errado.");
        } else {
            ID3D12Device* d3d12 = nullptr;
            if (SUCCEEDED(swapchain->GetDevice(__uuidof(ID3D12Device), reinterpret_cast<void**>(&d3d12)))) {
                d3d12->Release();
                auto* queue = find_queue(swapchain);
                render_d3d12(swapchain, queue);
                if (queue) queue->Release();
            } else {
                ID3D11Device* d3d11 = nullptr;
                if (SUCCEEDED(swapchain->GetDevice(__uuidof(ID3D11Device), reinterpret_cast<void**>(&d3d11)))) {
                    d3d11->Release();
                    render_d3d11(swapchain);
                } else {
                    ID3D10Device* d3d10 = nullptr;
                    if (SUCCEEDED(swapchain->GetDevice(__uuidof(ID3D10Device), reinterpret_cast<void**>(&d3d10)))) {
                        d3d10->Release();
                        render_d3d10(swapchain);
                    } else {
                        set_status(RenderApi::Unknown, RenderState::Unsupported,
                            "DXGI swapchain detectada, mas a API não é Direct3D 10, 11 nem 12.");
                    }
                }
            }
        }
      } catch (const std::exception& error) {
        set_status(RenderApi::Unknown, RenderState::Failed, std::string("Exceção no hook DXGI: ") + error.what());
      } catch (...) {
        set_status(RenderApi::Unknown, RenderState::Failed, "Exceção desconhecida no hook DXGI.");
      }
    }
    return g_present(swapchain, sync, flags);
}

ULONG STDMETHODCALLTYPE detour_release(IUnknown* object) {
    const auto remaining = g_release(object);
    if (remaining == 0 && object == g_active_swapchain) {
        std::lock_guard<std::mutex> render_lock(g_render_mutex);
        if (object == g_active_swapchain) {
            reset_renderer_devices();
            g_active_swapchain = nullptr;
            std::lock_guard<std::mutex> swapchain_lock(g_swapchain_mutex);
            auto found = g_queues.find(reinterpret_cast<IDXGISwapChain*>(object));
            if (found != g_queues.end()) {
                found->second->Release();
                g_queues.erase(found);
            }
        }
    }
    return remaining;
}

bool hook_address(void* target, void* detour, void** original) {
    if (!target) return false;
    const auto created = MH_CreateHook(target, detour, original);
    if (created != MH_OK && created != MH_ERROR_ALREADY_CREATED) return false;
    return MH_EnableHook(target) == MH_OK || MH_EnableHook(target) == MH_ERROR_ENABLED;
}

DWORD WINAPI initialize_hooks(void*) {
    InitializationSignal initialization_signal;
    const auto initialized = MH_Initialize();
    if (initialized != MH_OK && initialized != MH_ERROR_ALREADY_INITIALIZED) {
        set_status(RenderApi::Unknown, RenderState::Failed,
            "MinHook não conseguiu inicializar: " + std::to_string(initialized)
            + ". Feche e reinicie o jogo pelo PhantomMonitor; se persistir, verifique bloqueios de segurança.");
        return 1;
    }
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = DefWindowProcW;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"PhantomOverlayDxgiProbe";
    RegisterClassExW(&wc);
    HWND hwnd = CreateWindowExW(0, wc.lpszClassName, L"", WS_OVERLAPPEDWINDOW,
        0, 0, 64, 64, nullptr, nullptr, wc.hInstance, nullptr);
    DXGI_SWAP_CHAIN_DESC desc{};
    desc.BufferCount = 2;
    desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.OutputWindow = hwnd;
    desc.SampleDesc.Count = 1;
    desc.Windowed = TRUE;
    desc.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
    D3D_FEATURE_LEVEL level{};
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    IDXGISwapChain* swapchain = nullptr;
    const D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_0 };
    const auto probe = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
        levels, 2, D3D11_SDK_VERSION, &desc, &swapchain, &device, &level, &context);
    if (FAILED(probe)) {
        set_status(RenderApi::Unknown, RenderState::Failed,
            "Não foi possível criar swapchain de referência para instalar o hook DXGI: "
            + std::to_string(static_cast<unsigned long>(probe))
            + ". Feche e reinicie o jogo pelo PhantomMonitor; verifique o runtime gráfico.");
        if (hwnd) DestroyWindow(hwnd);
        return 1;
    }
    auto** swapchain_vtable = *reinterpret_cast<void***>(swapchain);
    bool success = hook_address(swapchain_vtable[2], reinterpret_cast<void*>(detour_release),
        reinterpret_cast<void**>(&g_release));
    success = hook_address(swapchain_vtable[8], reinterpret_cast<void*>(detour_present), reinterpret_cast<void**>(&g_present)) && success;
    success = hook_address(swapchain_vtable[13], reinterpret_cast<void*>(detour_resize_buffers),
        reinterpret_cast<void**>(&g_resize_buffers)) && success;
    IDXGIDevice* dxgi_device = nullptr;
    IDXGIAdapter* adapter = nullptr;
    IDXGIFactory* factory = nullptr;
    IDXGIFactory2* factory2 = nullptr;
    if (SUCCEEDED(device->QueryInterface(__uuidof(IDXGIDevice), reinterpret_cast<void**>(&dxgi_device)))
        && SUCCEEDED(dxgi_device->GetAdapter(&adapter))
        && SUCCEEDED(adapter->GetParent(__uuidof(IDXGIFactory), reinterpret_cast<void**>(&factory)))) {
        auto** factory_vtable = *reinterpret_cast<void***>(factory);
        success = hook_address(factory_vtable[10], reinterpret_cast<void*>(detour_create_swap_chain),
            reinterpret_cast<void**>(&g_create_swap_chain)) && success;
        if (SUCCEEDED(factory->QueryInterface(__uuidof(IDXGIFactory2), reinterpret_cast<void**>(&factory2)))) {
            auto** factory2_vtable = *reinterpret_cast<void***>(factory2);
            success = hook_address(factory2_vtable[15], reinterpret_cast<void*>(detour_create_swap_chain_for_hwnd),
                reinterpret_cast<void**>(&g_create_swap_chain_for_hwnd)) && success;
        }
    } else {
        success = false;
    }

    IDirect3D9* d3d9 = Direct3DCreate9(D3D_SDK_VERSION);
    IDirect3DDevice9* d3d9_device = nullptr;
    bool d3d9_hooked = false;
    if (d3d9 && hwnd) {
        D3DPRESENT_PARAMETERS parameters{};
        parameters.Windowed = TRUE;
        parameters.SwapEffect = D3DSWAPEFFECT_DISCARD;
        parameters.hDeviceWindow = hwnd;
        parameters.BackBufferWidth = 64;
        parameters.BackBufferHeight = 64;
        parameters.BackBufferFormat = D3DFMT_A8R8G8B8;
        parameters.PresentationInterval = D3DPRESENT_INTERVAL_IMMEDIATE;
        if (SUCCEEDED(d3d9->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, hwnd,
                D3DCREATE_SOFTWARE_VERTEXPROCESSING, &parameters, &d3d9_device))) {
            auto** d3d9_vtable = *reinterpret_cast<void***>(d3d9_device);
            d3d9_hooked = hook_address(d3d9_vtable[17], reinterpret_cast<void*>(detour_d3d9_present),
                reinterpret_cast<void**>(&g_d3d9_present));
            d3d9_hooked = hook_address(d3d9_vtable[16], reinterpret_cast<void*>(detour_d3d9_reset),
                reinterpret_cast<void**>(&g_d3d9_reset)) && d3d9_hooked;
        }
    }
    if (d3d9_device) d3d9_device->Release();
    if (d3d9) d3d9->Release();

    HWND gl_window = hwnd ? CreateWindowExW(0, wc.lpszClassName, L"", WS_OVERLAPPEDWINDOW,
        0, 0, 64, 64, nullptr, nullptr, wc.hInstance, nullptr) : nullptr;
    HDC gl_dc = gl_window ? GetDC(gl_window) : nullptr;
    HGLRC gl_context = nullptr;
    bool gl_hooked = false;
    if (gl_dc) {
        PIXELFORMATDESCRIPTOR pixel_format{};
        pixel_format.nSize = sizeof(pixel_format);
        pixel_format.nVersion = 1;
        pixel_format.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
        pixel_format.iPixelType = PFD_TYPE_RGBA;
        pixel_format.cColorBits = 32;
        pixel_format.cDepthBits = 24;
        pixel_format.iLayerType = PFD_MAIN_PLANE;
        const auto format = ChoosePixelFormat(gl_dc, &pixel_format);
        if (format != 0 && SetPixelFormat(gl_dc, format, &pixel_format)) {
            gl_context = wglCreateContext(gl_dc);
            if (gl_context && wglMakeCurrent(gl_dc, gl_context)) {
                HMODULE gdi = GetModuleHandleW(L"gdi32.dll");
                if (!gdi) gdi = LoadLibraryW(L"gdi32.dll");
                auto* swap_buffers = gdi ? reinterpret_cast<void*>(GetProcAddress(gdi, "SwapBuffers")) : nullptr;
                gl_hooked = hook_address(swap_buffers, reinterpret_cast<void*>(detour_gl_swap_buffers),
                    reinterpret_cast<void**>(&g_gl_swap_buffers));
                wglMakeCurrent(nullptr, nullptr);
            }
        }
        if (gl_context) wglDeleteContext(gl_context);
        ReleaseDC(gl_window, gl_dc);
    }
    if (gl_window) DestroyWindow(gl_window);

    if (factory2) factory2->Release();
    if (factory) factory->Release();
    if (adapter) adapter->Release();
    if (dxgi_device) dxgi_device->Release();
    swapchain->Release();
    context->Release();
    device->Release();
    DestroyWindow(hwnd);
    UnregisterClassW(wc.lpszClassName, wc.hInstance);
    if (!success) {
        set_status(RenderApi::Unknown, RenderState::Failed,
            "Não foi possível instalar todos os hooks Direct3D 9–12. Feche e reinicie o jogo pelo PhantomMonitor; verifique o runtime e bloqueios de segurança.");
        return 1;
    }
    std::string message = "Hooks DXGI para Direct3D 10–12 instalados neste executável autorizado.";
    message += d3d9_hooked ? " Hook Direct3D 9 ativo." : " O hook Direct3D 9 não foi preparado.";
    message += gl_hooked ? " Hook OpenGL/WGL ativo; aguardando apresentação."
                         : " O hook OpenGL/WGL não pôde ser preparado.";
    set_status(RenderApi::Unknown, RenderState::Waiting, message);
    return 0;
}

} // namespace

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
        if (HANDLE thread = CreateThread(nullptr, 0, initialize_hooks, nullptr, 0, nullptr))
            CloseHandle(thread);
    }
    return TRUE;
}
