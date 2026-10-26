cmake_minimum_required(VERSION 3.16)
project(PhantomVulkanLayer VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

find_path(PHANTOM_VULKAN_INCLUDE_DIR vulkan/vulkan.h
    HINTS "$ENV{VULKAN_SDK}/Include" "$ENV{VULKAN_SDK}/include")
if(NOT PHANTOM_VULKAN_INCLUDE_DIR)
    message(FATAL_ERROR "Install Vulkan headers (Vulkan SDK or libvulkan-dev). No loader import library is required.")
endif()
find_package(Threads REQUIRED)
add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../PhantomNativeOverlay
    ${CMAKE_CURRENT_BINARY_DIR}/PhantomNativeOverlay)

add_library(VkLayer_phantom_monitor SHARED
    PhantomVulkanLayer.h
    PhantomVulkanLayer.cpp
    PhantomVulkanTelemetry.h
    PhantomVulkanOverlay.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../PhantomNativeOverlay/third_party/imgui/backends/imgui_impl_vulkan.cpp
)

target_include_directories(VkLayer_phantom_monitor PRIVATE
    ${PHANTOM_VULKAN_INCLUDE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}
)

target_link_libraries(VkLayer_phantom_monitor PRIVATE
    PhantomOverlayCommon
    Threads::Threads
    ${CMAKE_DL_LIBS}
)
target_compile_definitions(VkLayer_phantom_monitor PRIVATE IMGUI_IMPL_VULKAN_NO_PROTOTYPES)
target_include_directories(VkLayer_phantom_monitor PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../PhantomNativeOverlay/third_party/imgui/backends
)

if(UNIX AND NOT APPLE)
    target_link_libraries(VkLayer_phantom_monitor PRIVATE rt)
endif()

if(WIN32)
    if(MSVC)
        if(CMAKE_SIZEOF_VOID_P EQUAL 4)
            target_sources(VkLayer_phantom_monitor PRIVATE exports-x86.def)
        endif()
        set_property(TARGET VkLayer_phantom_monitor
            PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    endif()
    configure_file(phantom_vulkan_layer_windows.json
        ${CMAKE_CURRENT_BINARY_DIR}/phantom_vulkan_layer.json COPYONLY)
else()
    configure_file(phantom_vulkan_layer.json
        ${CMAKE_CURRENT_BINARY_DIR}/phantom_vulkan_layer.json COPYONLY)
endif()

add_custom_command(TARGET VkLayer_phantom_monitor POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "${CMAKE_CURRENT_BINARY_DIR}/phantom_vulkan_layer.json"
        "$<TARGET_FILE_DIR:VkLayer_phantom_monitor>/phantom_vulkan_layer.json")

if(BUILD_TESTING)
    enable_testing()
    add_executable(PhantomVulkanDispatchTests tests/LayerDispatchTests.cpp)
    target_include_directories(PhantomVulkanDispatchTests PRIVATE ${PHANTOM_VULKAN_INCLUDE_DIR})
    target_link_libraries(PhantomVulkanDispatchTests PRIVATE VkLayer_phantom_monitor)
    if(MSVC)
        set_property(TARGET PhantomVulkanDispatchTests PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    endif()
    add_test(NAME PhantomVulkanDispatchTests COMMAND PhantomVulkanDispatchTests)
endif()

install(TARGETS VkLayer_phantom_monitor
    LIBRARY DESTINATION lib
    RUNTIME DESTINATION bin
)
