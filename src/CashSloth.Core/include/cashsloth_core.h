#pragma once

#if defined(_WIN32)
  #if defined(CS_BUILD_DLL)
    #define CS_API __declspec(dllexport)
  #else
    #define CS_API __declspec(dllimport)
  #endif
#else
  #define CS_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

CS_API int cs_init();
CS_API void cs_shutdown();

#ifdef __cplusplus
}
#endif
