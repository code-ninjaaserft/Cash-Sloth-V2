cmake_minimum_required(VERSION 3.20)

project(CashSlothV2 LANGUAGES CXX)

include(CTest)

add_subdirectory(src/CashSloth.Core)

# add_subdirectory(src/CashSloth.CoreApi)
if(BUILD_TESTING)
  add_subdirectory(tests/CashSloth.Core.Tests)
endif()
