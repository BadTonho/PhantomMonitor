cmake_minimum_required(VERSION 3.16)
project(PhantomVulkanLayer VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

find_package(Vulkan REQUIRED)
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
    ${Vulkan_INCLUDE_DIRS}
    ${CMAKE_CURRENT_SOURCE_DIR}
)

target_link_libraries(VkLayer_phantom_monitor PRIVATE
    PhantomOverlayCommon
    ${Vulkan_LIBRARIES}
    Threads::Threads
    ${CMAKE_DL_LIBS}
)
target_include_directories(VkLayer_phantom_monitor PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../PhantomNativeOverlay/third_party/imgui/backends
)

if(UNIX AND NOT APPLE)
    target_link_libraries(VkLayer_phantom_monitor PRIVATE rt)
endif()

if(WIN32)
    configure_file(phantom_vulkan_layer_windows.json
        ${CMAKE_CURRENT_BINARY_DIR}/phantom_vulkan_layer.json COPYONLY)
else()
    configure_file(phantom_vulkan_layer.json
        ${CMAKE_CURRENT_BINARY_DIR}/phantom_vulkan_layer.json COPYONLY)
endif()

install(TARGETS VkLayer_phantom_monitor
    LIBRARY DESTINATION lib
    RUNTIME DESTINATION bin
)
