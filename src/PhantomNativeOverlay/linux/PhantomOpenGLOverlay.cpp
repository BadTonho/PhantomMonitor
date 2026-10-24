#include "OverlayFrameReader.h"
#include "OverlayImGuiDraw.h"
#include "OverlayStatus.h"
#include "imgui.h"
#include "imgui_impl_opengl3.h"

#include <EGL/egl.h>
#include <GL/glx.h>
#include <dlfcn.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <mutex>
#include <string>

namespace {
using namespace phantom::overlay;
using GlxSwapBuffers = void (*)(Display*, GLXDrawable);
using EglSwapBuffers = EGLBoolean (*)(EGLDisplay, EGLSurface);
using GlxGetCurrentContext = GLXContext (*)();
using EglGetCurrentContext = EGLContext (*)();
using EglQuerySurface = EGLBoolean (*)(EGLDisplay, EGLSurface, EGLint, EGLint*);
using GlxQueryDrawable = void (*)(Display*, GLXDrawable, int, unsigned int*);

struct RendererState {
    ImGuiContext* context = nullptr;
    std::uintptr_t native_context = 0;
    bool initialized = false;
};

FrameReader g_reader;
StatusPublisher g_status(StatusProducer::OpenGL);
std::mutex g_mutex;
RendererState g_state;

template<typename T>
T next_symbol(const char* name) {
    return reinterpret_cast<T>(dlsym(RTLD_NEXT, name));
}

bool explicitly_enabled() {
    const auto* enabled = std::getenv("ENABLE_PHANTOM_OVERLAY");
    const auto* disabled = std::getenv("DISABLE_PHANTOM_OVERLAY");
    return enabled && std::strcmp(enabled, "1") == 0 && !(disabled && std::strcmp(disabled, "1") == 0);
}

void set_status(RenderState state, const std::string& message) noexcept {
    try { g_status.Update(RenderApi::OpenGL, state, message); }
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
    const auto original = next_symbol<GlxSwapBuffers>("glXSwapBuffers");
    if (!original) return;
    if (explicitly_enabled()) {
        const auto get_context = next_symbol<GlxGetCurrentContext>("glXGetCurrentContext");
        const auto query_drawable = next_symbol<GlxQueryDrawable>("glXQueryDrawable");
        if (get_context && query_drawable) {
            unsigned int width = 0;
            unsigned int height = 0;
            query_drawable(display, drawable, 0x801D, &width);  // GLX_WIDTH
            query_drawable(display, drawable, 0x801E, &height); // GLX_HEIGHT
            try { draw(reinterpret_cast<std::uintptr_t>(get_context()), width, height, false); }
            catch (...) { set_status(RenderState::Failed, "Exceção no renderer OpenGL/GLX."); }
        }
    }
    original(display, drawable);
}

extern "C" __attribute__((visibility("default"))) EGLBoolean eglSwapBuffers(EGLDisplay display, EGLSurface surface) {
    const auto original = next_symbol<EglSwapBuffers>("eglSwapBuffers");
    if (!original) return EGL_FALSE;
    if (explicitly_enabled()) {
        const auto get_context = next_symbol<EglGetCurrentContext>("eglGetCurrentContext");
        const auto query_surface = next_symbol<EglQuerySurface>("eglQuerySurface");
        if (get_context && query_surface) {
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
                    return original(display, surface);
                }
            }
            if (query_surface(display, surface, EGL_WIDTH, &width)
                && query_surface(display, surface, EGL_HEIGHT, &height))
                try {
                    draw(reinterpret_cast<std::uintptr_t>(context), static_cast<std::uint32_t>(std::max(0, width)),
                        static_cast<std::uint32_t>(std::max(0, height)), gles);
                } catch (...) {
                    set_status(RenderState::Failed, "Exceção no renderer OpenGL/EGL.");
                }
        }
    }
    return original(display, surface);
}
