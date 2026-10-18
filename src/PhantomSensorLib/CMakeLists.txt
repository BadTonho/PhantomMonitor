cmake_minimum_required(VERSION 3.14)
project(PhantomSensorLib VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(PhantomSensorLib SHARED
    PhantomSensorLib.h
    PhantomSensorLib.cpp
)

target_compile_definitions(PhantomSensorLib PRIVATE PHANTOMSENSORLIB_EXPORTS)

install(TARGETS PhantomSensorLib
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
)
