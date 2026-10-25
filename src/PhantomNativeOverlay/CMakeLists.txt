cmake_minimum_required(VERSION 3.16)
project(PhantomOverlayNative LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(IMGUI_DIR ${CMAKE_CURRENT_SOURCE_DIR}/third_party/imgui)
add_library(PhantomOverlayCommon STATIC
    OverlayFrame.cpp
    OverlayFrameReader.cpp
    OverlayImGuiDraw.cpp
    OverlayStatus.cpp
    ${IMGUI_DIR}/imgui.cpp
    ${IMGUI_DIR}/imgui_draw.cpp
    ${IMGUI_DIR}/imgui_tables.cpp
    ${IMGUI_DIR}/imgui_widgets.cpp
)
target_include_directories(PhantomOverlayCommon PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${IMGUI_DIR}
)
if(UNIX AND NOT APPLE)
    target_link_libraries(PhantomOverlayCommon PUBLIC rt)

    find_package(OpenGL REQUIRED COMPONENTS OpenGL GLX EGL)
    find_package(Threads REQUIRED)
    add_library(PhantomOpenGLOverlay SHARED
        linux/PhantomOpenGLOverlay.cpp
        ${IMGUI_DIR}/backends/imgui_impl_opengl3.cpp
    )
    target_include_directories(PhantomOpenGLOverlay PRIVATE ${IMGUI_DIR}/backends)
    target_link_libraries(PhantomOpenGLOverlay PRIVATE
        PhantomOverlayCommon OpenGL::GLX OpenGL::EGL Threads::Threads ${CMAKE_DL_LIBS}
    )
    set_target_properties(PhantomOpenGLOverlay PROPERTIES OUTPUT_NAME PhantomOpenGLOverlay)
endif()

if(WIN32)
    add_library(PhantomOverlayMinHook STATIC
        third_party/minhook/src/buffer.c
        third_party/minhook/src/hook.c
        third_party/minhook/src/trampoline.c
        $<$<EQUAL:${CMAKE_SIZEOF_VOID_P},8>:third_party/minhook/src/hde/hde64.c>
        $<$<EQUAL:${CMAKE_SIZEOF_VOID_P},4>:third_party/minhook/src/hde/hde32.c>
    )
    target_include_directories(PhantomOverlayMinHook PUBLIC third_party/minhook/include)
    target_include_directories(PhantomOverlayMinHook PRIVATE
        third_party/minhook/src
        third_party/minhook/src/hde
    )

    add_library(PhantomOverlayHook SHARED
        windows/PhantomOverlayWindows.cpp
        ${IMGUI_DIR}/backends/imgui_impl_dx9.cpp
        ${IMGUI_DIR}/backends/imgui_impl_dx10.cpp
        ${IMGUI_DIR}/backends/imgui_impl_dx11.cpp
        ${IMGUI_DIR}/backends/imgui_impl_dx12.cpp
        ${IMGUI_DIR}/backends/imgui_impl_opengl3.cpp
    )
    target_include_directories(PhantomOverlayHook PRIVATE
        ${IMGUI_DIR}/backends
        third_party/minhook/include
    )
    target_link_libraries(PhantomOverlayHook PRIVATE
        PhantomOverlayCommon
        PhantomOverlayMinHook
        d3d9 d3d10 d3d11 d3d12 dxgi d3dcompiler opengl32 gdi32 user32
    )
    target_compile_definitions(PhantomOverlayHook PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
    set_target_properties(PhantomOverlayHook PROPERTIES OUTPUT_NAME PhantomOverlayHook)

    add_executable(PhantomOverlayLauncher windows/PhantomOverlayLauncher.cpp)
    target_compile_definitions(PhantomOverlayLauncher PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
    set_target_properties(PhantomOverlayLauncher PROPERTIES OUTPUT_NAME PhantomOverlayLauncher)

    if(MSVC)
        set_property(TARGET PhantomOverlayCommon PhantomOverlayMinHook PhantomOverlayHook PhantomOverlayLauncher
            PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    endif()
endif()

include(CTest)
if(BUILD_TESTING)
    add_executable(PhantomOverlayNativeTests tests/PhantomOverlayNativeTests.cpp)
    target_link_libraries(PhantomOverlayNativeTests PRIVATE PhantomOverlayCommon)
    if(WIN32 AND MSVC)
        set_property(TARGET PhantomOverlayNativeTests
            PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    endif()
    add_test(NAME PhantomOverlayNativeTests COMMAND PhantomOverlayNativeTests)
endif()
