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

enum {
  CS_SUCCESS = 0,
  CS_ERROR_INVALID_ARGUMENT = 1,
  CS_ERROR_OUT_OF_MEMORY = 2,
  CS_ERROR_INTERNAL = 100
};

CS_API int cs_init();
CS_API void cs_shutdown();
CS_API const char* cs_last_error();
CS_API void cs_free(void* p);
CS_API int cs_get_version(char** out_json);

#ifdef __cplusplus
}
#endif
