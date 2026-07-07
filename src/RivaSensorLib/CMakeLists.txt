cmake_minimum_required(VERSION 3.14)
project(RivaSensorLib VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(RivaSensorLib SHARED
    RivaSensorLib.h
    RivaSensorLib.cpp
)

target_compile_definitions(RivaSensorLib PRIVATE RIVASENSORLIB_EXPORTS)

install(TARGETS RivaSensorLib
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
)
