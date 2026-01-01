cmake_minimum_required(VERSION 3.16)
project(meshopt_wrapper VERSION 1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Fetch meshoptimizer from GitHub
include(FetchContent)
FetchContent_Declare(
    meshoptimizer
    GIT_REPOSITORY https://github.com/zeux/meshoptimizer.git
    GIT_TAG        v0.21
)
FetchContent_MakeAvailable(meshoptimizer)

# Create shared library
add_library(meshopt_wrapper SHARED
    meshopt_wrapper.cpp
    meshopt_wrapper.h
)

# Link meshoptimizer (static)
target_link_libraries(meshopt_wrapper PRIVATE meshoptimizer)

# Include directories
target_include_directories(meshopt_wrapper PRIVATE
    ${meshoptimizer_SOURCE_DIR}/src
)

# Export symbols
target_compile_definitions(meshopt_wrapper PRIVATE MESHOPT_WRAPPER_EXPORTS)

# Set output name without 'lib' prefix on Windows
set_target_properties(meshopt_wrapper PROPERTIES
    PREFIX ""
    OUTPUT_NAME "meshopt_wrapper"
)

# Windows-specific settings
if(WIN32)
    target_compile_definitions(meshopt_wrapper PRIVATE _CRT_SECURE_NO_WARNINGS)
endif()

# Install
install(TARGETS meshopt_wrapper
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
)
install(FILES meshopt_wrapper.h DESTINATION include)
