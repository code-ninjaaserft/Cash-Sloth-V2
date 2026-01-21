cmake_minimum_required(VERSION 3.20)

project(CashSlothCore LANGUAGES CXX)

add_library(CashSlothCore SHARED
  src/core.cpp
)

target_include_directories(CashSlothCore
  PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_compile_definitions(CashSlothCore PRIVATE CS_BUILD_DLL)

target_compile_features(CashSlothCore PRIVATE cxx_std_17)

set_target_properties(CashSlothCore PROPERTIES
  OUTPUT_NAME "CashSlothCore"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
  LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
  ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)
