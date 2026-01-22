#include "cashsloth_core.h"

#include <cstring>
#include <iostream>

bool check(bool condition, const char* message) {
  if (!condition) {
    std::cerr << message << "\n";
    return false;
  }
  return true;
}

int main() {
  if (cs_init() != CS_SUCCESS) {
    std::cerr << "cs_init failed: " << cs_last_error() << "\n";
    return 1;
  }

  const char* valid_catalog =
      "{\"items\":[{\"id\":\"COFFEE\",\"name\":\"Coffee\",\"unit_cents\":500},"
      "{\"id\":\"TEA\",\"name\":\"Tea\",\"unit_cents\":400}]}";
  if (!check(cs_catalog_load_json(valid_catalog) == CS_SUCCESS,
             "cs_catalog_load_json valid catalog failed.")) {
    cs_shutdown();
    return 1;
  }

  char* catalog_json = nullptr;
  if (!check(cs_catalog_get_json(&catalog_json) == CS_SUCCESS,
             "cs_catalog_get_json failed after valid load.")) {
    cs_shutdown();
    return 1;
  }
  if (!check(catalog_json != nullptr && std::strlen(catalog_json) > 0,
             "cs_catalog_get_json returned empty JSON.")) {
    cs_free(catalog_json);
    cs_shutdown();
    return 1;
  }
  cs_free(catalog_json);

  cs_cart_t cart = nullptr;
  if (!check(cs_cart_new(&cart) == CS_SUCCESS, "cs_cart_new failed.")) {
    cs_shutdown();
    return 1;
  }

  if (!check(cs_cart_add_item_by_id(cart, "COFFEE", 1) == CS_SUCCESS,
             "cs_cart_add_item_by_id COFFEE failed after catalog load.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_cart_add_item_by_id(cart, "UNKNOWN", 1) == CS_ERROR_INVALID_ARGUMENT,
             "Unknown item should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strlen(cs_last_error()) > 0,
             "cs_last_error should be set for unknown item.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_catalog_load_json(nullptr) == CS_ERROR_INVALID_ARGUMENT,
             "Null catalog JSON should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_catalog_load_json("") == CS_ERROR_INVALID_ARGUMENT,
             "Empty catalog JSON should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_catalog_load_json("{invalid") == CS_ERROR_INVALID_ARGUMENT,
             "Invalid catalog JSON should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_catalog_get_json(&catalog_json) == CS_SUCCESS,
             "cs_catalog_get_json failed after invalid load.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(catalog_json, "\"id\":\"COFFEE\"") != nullptr,
             "Catalog should retain previous items after invalid load.")) {
    cs_free(catalog_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  cs_free(catalog_json);

  const char* duplicate_catalog =
      "{\"items\":[{\"id\":\"COFFEE\",\"name\":\"Coffee\",\"unit_cents\":500},"
      "{\"id\":\"COFFEE\",\"name\":\"Coffee\",\"unit_cents\":600}]}";
  if (!check(cs_catalog_load_json(duplicate_catalog) == CS_ERROR_INVALID_ARGUMENT,
             "Duplicate catalog ids should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  const char* replacement_catalog =
      "{\"items\":[{\"id\":\"TEA\",\"name\":\"Tea\",\"unit_cents\":400}]}";
  if (!check(cs_catalog_load_json(replacement_catalog) == CS_SUCCESS,
             "Catalog reload failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_cart_add_item_by_id(cart, "TEA", 1) == CS_SUCCESS,
             "TEA should be available after reload.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_cart_add_item_by_id(cart, "COFFEE", 1) == CS_ERROR_INVALID_ARGUMENT,
             "COFFEE should be unknown after reload.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  long long total_cents = 0;
  if (!check(cs_cart_get_total_cents(cart, &total_cents) == CS_SUCCESS,
             "cs_cart_get_total_cents failed after reload.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(total_cents == 900,
             "Total should include existing lines even after catalog reload.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  cs_cart_free(cart);
  cs_shutdown();
  return 0;
}
