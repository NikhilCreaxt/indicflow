cmake_minimum_required(VERSION 3.18)
project(HindiHarfBuzzBridge C)

set(BRIDGE_SRC ${CMAKE_CURRENT_LIST_DIR}/build_src~/hindi_harfbuzz_bridge.c)
add_library(HindiHarfBuzz SHARED ${BRIDGE_SRC})

target_include_directories(HindiHarfBuzz PRIVATE ${HARFBUZZ_INCLUDE_DIR})

target_link_libraries(HindiHarfBuzz PRIVATE ${HARFBUZZ_LIBRARY})

if(ANDROID)
  # Keep the shipped Android bridge loadable on 16 KB page-size devices.
  target_link_options(HindiHarfBuzz PRIVATE "LINKER:-z,max-page-size=16384")
endif()
