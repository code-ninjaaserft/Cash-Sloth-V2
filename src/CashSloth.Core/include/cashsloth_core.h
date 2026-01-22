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

typedef void* cs_cart_t;

CS_API int cs_init();
CS_API void cs_shutdown();
CS_API const char* cs_last_error();
CS_API void cs_free(void* p);
CS_API int cs_get_version(char** out_json);
CS_API int cs_catalog_load_json(const char* json);
CS_API int cs_catalog_get_json(char** out_json);

CS_API int cs_cart_new(cs_cart_t* out_cart);
CS_API int cs_cart_free(cs_cart_t cart);
CS_API int cs_cart_clear(cs_cart_t cart);
CS_API int cs_cart_add_item_by_id(cs_cart_t cart, const char* item_id, int qty);
CS_API int cs_cart_remove_line(cs_cart_t cart, int line_index);
CS_API int cs_cart_get_total_cents(cs_cart_t cart, long long* out_total_cents);
CS_API int cs_cart_get_lines_json(cs_cart_t cart, char** out_json);
CS_API int cs_payment_set_given_cents(cs_cart_t cart, long long given_cents);
CS_API int cs_payment_get_change_cents(cs_cart_t cart, long long* out_change_cents);
CS_API int cs_payment_get_given_cents(cs_cart_t cart, long long* out_given_cents);

#ifdef __cplusplus
}
#endif
