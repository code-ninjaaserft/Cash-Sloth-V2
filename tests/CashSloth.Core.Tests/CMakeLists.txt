cmake_minimum_required(VERSION 3.20)

project(CashSlothCoreTests LANGUAGES CXX)

add_executable(CashSlothCoreContractTests
  version_contract_test.cpp
)

add_executable(CashSlothCoreCatalogContractTests
  catalog_contract_test.cpp
)

add_executable(CashSlothCoreCartContractTests
  cart_contract_test.cpp
)

add_executable(CashSlothCorePaymentContractTests
  payment_contract_test.cpp
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

target_include_directories(CashSlothCoreCatalogContractTests
  PRIVATE
    ${CMAKE_SOURCE_DIR}/src/CashSloth.Core/include
)

target_link_libraries(CashSlothCoreCatalogContractTests PRIVATE CashSlothCore)

target_compile_features(CashSlothCoreCatalogContractTests PRIVATE cxx_std_17)

set_target_properties(CashSlothCoreCatalogContractTests PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)

add_test(NAME CashSlothCoreCatalogContractTests COMMAND $<TARGET_FILE:CashSlothCoreCatalogContractTests>)

target_include_directories(CashSlothCoreCartContractTests
  PRIVATE
    ${CMAKE_SOURCE_DIR}/src/CashSloth.Core/include
)

target_link_libraries(CashSlothCoreCartContractTests PRIVATE CashSlothCore)

target_compile_features(CashSlothCoreCartContractTests PRIVATE cxx_std_17)

set_target_properties(CashSlothCoreCartContractTests PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)

add_test(NAME CashSlothCoreCartContractTests COMMAND $<TARGET_FILE:CashSlothCoreCartContractTests>)

target_include_directories(CashSlothCorePaymentContractTests
  PRIVATE
    ${CMAKE_SOURCE_DIR}/src/CashSloth.Core/include
)

target_link_libraries(CashSlothCorePaymentContractTests PRIVATE CashSlothCore)

target_compile_features(CashSlothCorePaymentContractTests PRIVATE cxx_std_17)

set_target_properties(CashSlothCorePaymentContractTests PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)

add_test(NAME CashSlothCorePaymentContractTests COMMAND $<TARGET_FILE:CashSlothCorePaymentContractTests>)
