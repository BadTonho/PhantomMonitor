cmake_minimum_required(VERSION 3.16)
project(PhantomVulkanLayer VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

find_package(Vulkan REQUIRED)
find_package(Threads REQUIRED)

add_library(VkLayer_phantom_monitor SHARED
    PhantomVulkanLayer.h
    PhantomVulkanLayer.cpp
    PhantomVulkanTelemetry.h
)

target_include_directories(VkLayer_phantom_monitor PRIVATE
    ${Vulkan_INCLUDE_DIRS}
    ${CMAKE_CURRENT_SOURCE_DIR}
)

target_link_libraries(VkLayer_phantom_monitor PRIVATE
    ${Vulkan_LIBRARIES}
    Threads::Threads
    ${CMAKE_DL_LIBS}
)

if(UNIX AND NOT APPLE)
    target_link_libraries(VkLayer_phantom_monitor PRIVATE rt)
endif()

configure_file(
    ${CMAKE_CURRENT_SOURCE_DIR}/phantom_vulkan_layer.json
    ${CMAKE_CURRENT_BINARY_DIR}/phantom_vulkan_layer.json
    COPYONLY
)

install(TARGETS VkLayer_phantom_monitor
    LIBRARY DESTINATION lib
)
