#include "OverlayFrameReader.h"
#include "OverlayImGuiDraw.h"
#include "OverlayStatus.h"
#include "PhantomOpenGLTelemetry.h"
#include "imgui.h"
#include "imgui_impl_opengl3.h"

#include <EGL/egl.h>
#include <GL/glx.h>
#include <dlfcn.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <mutex>
#include <string>

namespace {
using namespace phantom::overlay;
using phantom::overlay::linux_fps::Publisher;
using GlxSwapBuffers = void (*)(Display*, GLXDrawable);
using EglSwapBuffers = EGLBoolean (*)(EGLDisplay, EGLSurface);
using GlxGetCurrentContext = GLXContext (*)();
using EglGetCurrentContext = EGLContext (*)();
using EglQuerySurface = EGLBoolean (*)(EGLDisplay, EGLSurface, EGLint, EGLint*);
using GlxQueryDrawable = void (*)(Display*, GLXDrawable, int, unsigned int*);
using GlGetIntegerv = void (*)(unsigned int, int*);

struct RendererState {
    ImGuiContext* context = nullptr;
    std::uintptr_t native_context = 0;
    bool initialized = false;
};

FrameReader g_reader;
std::mutex g_mutex;
RendererState g_state;
Publisher g_fps_publisher;

// O arquivo de status em /dev/shm só é criado quando este processo realmente apresenta um quadro GL;
// criá-lo no carregamento da biblioteca geraria um arquivo para cada processo da sessão (LD_PRELOAD global).
StatusPublisher& status_publisher() {
    static StatusPublisher publisher(StatusProducer::OpenGL);
    return publisher;
}

using DlsymFn = void* (*)(void*, const char*);

// Esta biblioteca exporta `dlsym` (veja o fim do arquivo); a função real vem do libc por versão de símbolo.
DlsymFn real_dlsym() {
    static const DlsymFn fn = [] {
        for (const char* version : {"GLIBC_2.34", "GLIBC_2.2.5", "GLIBC_2.17"}) {
            if (void* symbol = dlvsym(RTLD_NEXT, "dlsym", version)) return reinterpret_cast<DlsymFn>(symbol);
        }
        return static_cast<DlsymFn>(nullptr);
    }();
    return fn;
}

template<typename T>
T next_symbol(const char* name) {
    const auto lookup = real_dlsym();
    return lookup ? reinterpret_cast<T>(lookup(RTLD_NEXT, name)) : nullptr;
}

// Processos do próprio desktop (compositor, shell, greeter) usam GL/EGL, mas nunca são jogos:
// o hook fica em passagem direta neles, sem telemetria, sem renderer e sem arquivos em /dev/shm.
bool is_desktop_process() {
    static const bool desktop = [] {
        char name[64] = {};
        FILE* file = std::fopen("/proc/self/comm", "re");
        if (!file) return false;
        const bool read_ok = std::fgets(name, sizeof(name), file) != nullptr;
        std::fclose(file);
        if (!read_ok) return false;
        name[std::strcspn(name, "\n")] = '\0';
        static const char* const kPrefixes[] = {
            "plasma", "kwin", "kded", "krunner", "ksplash", "kscreen", "kglobalacceld", "ksmserver",
            "gnome-shell", "gnome-session", "mutter", "gdm", "sddm", "Xwayland", "Xorg", "Xephyr",
            "xdg-", "baloo", "polkit", "xwaylandvideo"};
        for (const char* prefix : kPrefixes) {
            if (std::strncmp(name, prefix, std::strlen(prefix)) == 0) return true;
        }
        return false;
    }();
    return desktop;
}

// Ponteiros reais obtidos quando o app resolve os símbolos por dlsym(handle, ...) (GLFW/LWJGL/SDL).
std::atomic<void*> g_real_glx_swap{nullptr};
std::atomic<void*> g_real_egl_swap{nullptr};

bool try_get_viewport_dimensions(unsigned int& width, unsigned int& height) {
    const auto get_integerv = next_symbol<GlGetIntegerv>("glGetIntegerv");
    if (!get_integerv) return false;
    int viewport[4] = {0, 0, 0, 0};
    get_integerv(0x0BA2, viewport); // GL_VIEWPORT
    if (viewport[2] > 0 && viewport[3] > 0) {
        width = static_cast<unsigned int>(viewport[2]);
        height = static_cast<unsigned int>(viewport[3]);
        return true;
    }
    return false;
}

bool explicitly_enabled() {
    const auto* enabled = std::getenv("ENABLE_PHANTOM_OVERLAY");
    const auto* disabled = std::getenv("DISABLE_PHANTOM_OVERLAY");
    return enabled && std::strcmp(enabled, "1") == 0 && !(disabled && std::strcmp(disabled, "1") == 0);
}

void set_status(RenderState state, const std::string& message) noexcept {
    try { status_publisher().Update(RenderApi::OpenGL, state, message); }
    catch (...) { }
}

void draw(std::uintptr_t native_context, std::uint32_t width, std::uint32_t height, bool gles) {
    if (!explicitly_enabled() || native_context == 0) return;
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_state.native_context != native_context) {
        if (g_state.context != nullptr) {
            set_status(RenderState::Unsupported,
                "Mais de um contexto OpenGL foi apresentado; o renderer mantém o primeiro para evitar misturar recursos.");
            return;
        }
        g_state.context = ImGui::CreateContext();
        g_state.native_context = native_context;
        if (!g_state.context) {
            set_status(RenderState::Failed, "Não foi possível criar o contexto Dear ImGui para OpenGL.");
            return;
        }
        ImGui::SetCurrentContext(g_state.context);
        ConfigureOverlayFonts();
        if (!ImGui_ImplOpenGL3_Init(gles ? "#version 300 es" : "#version 130")) {
            set_status(RenderState::Failed, "Dear ImGui não inicializou o backend OpenGL3.");
            return;
        }
        g_state.initialized = true;
    }
    if (!g_state.initialized) return;

    Frame frame;
    std::string error;
    if (!g_reader.TryRead(frame, error)) {
        set_status(error == "frame is stale" ? RenderState::Stale : RenderState::Waiting,
            error == "frame is stale" ? "Quadro aplicado expirou; nada foi desenhado."
                                       : "Aguardando quadro aplicado válido: " + error);
        return;
    }
    if (!frame.visible) {
        set_status(RenderState::Hidden, "Overlay dentro do jogo oculto pelo PhantomMonitor.");
        return;
    }
    if (width == 0 || height == 0 || frame.reference_width <= 0 || frame.reference_height <= 0) {
        set_status(RenderState::Waiting, "A superfície OpenGL ainda não tem dimensões válidas.");
        return;
    }

    ImGui::SetCurrentContext(g_state.context);
    ImGui_ImplOpenGL3_NewFrame();
    ImDrawData* draw_data = nullptr;
    DrawFrame(frame, width, height, static_cast<float>(width) / frame.reference_width,
        static_cast<float>(height) / frame.reference_height, &draw_data);
    if (draw_data) {
        ImGui_ImplOpenGL3_RenderDrawData(draw_data);
        set_status(RenderState::Active, "Renderer OpenGL desenhando o quadro aplicado.");
    }
}

} // namespace

extern "C" __attribute__((visibility("default"))) void glXSwapBuffers(Display* display, GLXDrawable drawable) {
    auto original = reinterpret_cast<GlxSwapBuffers>(g_real_glx_swap.load(std::memory_order_acquire));
    if (!original) original = next_symbol<GlxSwapBuffers>("glXSwapBuffers");
    if (!original) return;
    if (is_desktop_process()) { original(display, drawable); return; }
    if (explicitly_enabled()) {
        const auto get_context = next_symbol<GlxGetCurrentContext>("glXGetCurrentContext");
        const auto query_drawable = next_symbol<GlxQueryDrawable>("glXQueryDrawable");
        if (get_context) {
            unsigned int width = 0;
            unsigned int height = 0;
            if (query_drawable) {
                query_drawable(display, drawable, 0x801D, &width);  // GLX_WIDTH
                query_drawable(display, drawable, 0x801E, &height); // GLX_HEIGHT
            }
            if (width == 0 || height == 0) {
                try_get_viewport_dimensions(width, height);
            }
            try { draw(reinterpret_cast<std::uintptr_t>(get_context()), width, height, false); }
            catch (...) { set_status(RenderState::Failed, "Exceção no renderer OpenGL/GLX."); }
        }
    }
    original(display, drawable);
    g_fps_publisher.RecordPresent();
}

extern "C" __attribute__((visibility("default"))) EGLBoolean eglSwapBuffers(EGLDisplay display, EGLSurface surface) {
    auto original = reinterpret_cast<EglSwapBuffers>(g_real_egl_swap.load(std::memory_order_acquire));
    if (!original) original = next_symbol<EglSwapBuffers>("eglSwapBuffers");
    if (!original) return EGL_FALSE;
    if (is_desktop_process()) return original(display, surface);
    if (explicitly_enabled()) {
        const auto get_context = next_symbol<EglGetCurrentContext>("eglGetCurrentContext");
        const auto query_surface = next_symbol<EglQuerySurface>("eglQuerySurface");
        if (get_context) {
            EGLint width = 0;
            EGLint height = 0;
            const auto context = get_context();
            const auto gles = eglQueryAPI() == EGL_OPENGL_ES_API;
            if (gles) {
                EGLint client_version = 0;
                if (!context || !eglQueryContext(display, context, EGL_CONTEXT_CLIENT_VERSION, &client_version)
                    || client_version < 3) {
                    set_status(RenderState::Unsupported,
                        "O renderer OpenGL/EGL exige um contexto OpenGL ES 3.0 ou superior.");
                    const auto result = original(display, surface);
                    if (result == EGL_TRUE) g_fps_publisher.RecordPresent();
                    return result;
                }
            }
            if (query_surface) {
                query_surface(display, surface, EGL_WIDTH, &width);
                query_surface(display, surface, EGL_HEIGHT, &height);
            }
            if (width <= 0 || height <= 0) {
                unsigned int vp_width = 0;
                unsigned int vp_height = 0;
                if (try_get_viewport_dimensions(vp_width, vp_height)) {
                    width = static_cast<EGLint>(vp_width);
                    height = static_cast<EGLint>(vp_height);
                }
            }
            try {
                draw(reinterpret_cast<std::uintptr_t>(context), static_cast<std::uint32_t>(std::max(0, width)),
                    static_cast<std::uint32_t>(std::max(0, height)), gles);
            } catch (...) {
                set_status(RenderState::Failed, "Exceção no renderer OpenGL/EGL.");
            }
        }
    }
    const auto result = original(display, surface);
    if (result == EGL_TRUE) g_fps_publisher.RecordPresent();
    return result;
}

// --- Interceptação de resolução dinâmica ---------------------------------------------------------
// GLFW (Minecraft/LWJGL), SDL e vários engines abrem a libGL/libEGL com dlopen e obtêm
// glXSwapBuffers/eglSwapBuffers por dlsym(handle, ...), o que ignora um LD_PRELOAD comum. Quando o app
// pede esses símbolos explicitamente, devolvemos os wrappers acima; as demais chamadas passam direto.

namespace {
using GlProc = void (*)();

void* wrap_symbol(const char* name, void* real) {
    if (!real || !name || is_desktop_process()) return real;
    if (std::strcmp(name, "glXSwapBuffers") == 0) {
        if (real != reinterpret_cast<void*>(&glXSwapBuffers)) g_real_glx_swap.store(real, std::memory_order_release);
        return reinterpret_cast<void*>(&glXSwapBuffers);
    }
    if (std::strcmp(name, "eglSwapBuffers") == 0) {
        if (real != reinterpret_cast<void*>(&eglSwapBuffers)) g_real_egl_swap.store(real, std::memory_order_release);
        return reinterpret_cast<void*>(&eglSwapBuffers);
    }
    return real;
}
} // namespace

extern "C" __attribute__((visibility("default"))) void* dlsym(void* handle, const char* name) {
    const auto lookup = real_dlsym();
    if (!lookup) return nullptr;
    void* result = lookup(handle, name);
    // RTLD_NEXT vindo de outro interposer (ex.: outra camada) mantém a semântica original.
    if (handle == RTLD_NEXT) return result;
    return wrap_symbol(name, result);
}

extern "C" __attribute__((visibility("default"))) __GLXextFuncPtr glXGetProcAddress(const GLubyte* name) {
    using Fn = __GLXextFuncPtr (*)(const GLubyte*);
    const auto original = next_symbol<Fn>("glXGetProcAddress");
    if (!original) return nullptr;
    void* result = reinterpret_cast<void*>(original(name));
    return reinterpret_cast<__GLXextFuncPtr>(wrap_symbol(reinterpret_cast<const char*>(name), result));
}

extern "C" __attribute__((visibility("default"))) __GLXextFuncPtr glXGetProcAddressARB(const GLubyte* name) {
    using Fn = __GLXextFuncPtr (*)(const GLubyte*);
    auto original = next_symbol<Fn>("glXGetProcAddressARB");
    if (!original) original = next_symbol<Fn>("glXGetProcAddress");
    if (!original) return nullptr;
    void* result = reinterpret_cast<void*>(original(name));
    return reinterpret_cast<__GLXextFuncPtr>(wrap_symbol(reinterpret_cast<const char*>(name), result));
}

extern "C" __attribute__((visibility("default"))) __eglMustCastToProperFunctionPointerType eglGetProcAddress(const char* name) {
    using Fn = __eglMustCastToProperFunctionPointerType (*)(const char*);
    const auto original = next_symbol<Fn>("eglGetProcAddress");
    if (!original) return nullptr;
    void* result = reinterpret_cast<void*>(original(name));
    return reinterpret_cast<__eglMustCastToProperFunctionPointerType>(wrap_symbol(name, result));
}
