cmake_minimum_required(VERSION 3.20)

project(CashSlothCoreTests LANGUAGES CXX)

add_executable(CashSlothCoreContractTests
  version_contract_test.cpp
)

target_include_directories(CashSlothCoreContractTests
  PRIVATE
    ${CMAKE_SOURCE_DIR}/src/CashSloth.Core/include
)

target_link_libraries(CashSlothCoreContractTests PRIVATE CashSlothCore)

target_compile_features(CashSlothCoreContractTests PRIVATE cxx_std_17)

set_target_properties(CashSlothCoreContractTests PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)

add_test(NAME CashSlothCoreContractTests COMMAND $<TARGET_FILE:CashSlothCoreContractTests>)
