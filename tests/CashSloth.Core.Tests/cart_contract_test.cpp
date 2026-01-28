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

  const char* catalog_json =
      "{\"items\":[{\"id\":\"COFFEE\",\"name\":\"Coffee\",\"unit_cents\":500},"
      "{\"id\":\"TEA\",\"name\":\"Tea\",\"unit_cents\":400}]}";
  if (!check(cs_catalog_load_json(catalog_json) == CS_SUCCESS,
             "cs_catalog_load_json failed.")) {
    cs_shutdown();
    return 1;
  }

  cs_cart_t cart = nullptr;
  if (!check(cs_cart_new(&cart) == CS_SUCCESS, "cs_cart_new failed.")) {
    cs_shutdown();
    return 1;
  }
  if (!check(cart != nullptr, "cs_cart_new returned null cart.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  char* empty_json = nullptr;
  if (!check(cs_cart_get_lines_json(cart, &empty_json) == CS_SUCCESS,
             "cs_cart_get_lines_json for empty cart failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(empty_json != nullptr && std::strlen(empty_json) > 0,
             "cs_cart_get_lines_json for empty cart returned empty JSON.")) {
    cs_free(empty_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(empty_json, "\"total_cents\":0") != nullptr,
             "Empty cart JSON missing total_cents 0.")) {
    cs_free(empty_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(empty_json, "\"given_cents\":0") != nullptr,
             "Empty cart JSON missing given_cents 0.")) {
    cs_free(empty_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(empty_json, "\"change_cents\":0") != nullptr,
             "Empty cart JSON missing change_cents 0.")) {
    cs_free(empty_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(empty_json, "\"lines\":[]") != nullptr,
             "Empty cart JSON missing empty lines array.")) {
    cs_free(empty_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  cs_free(empty_json);

  if (!check(cs_cart_add_item_by_id(cart, "COFFEE", 2) == CS_SUCCESS,
             "cs_cart_add_item_by_id COFFEE qty 2 failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  long long total_cents = 0;
  if (!check(cs_cart_get_total_cents(cart, &total_cents) == CS_SUCCESS,
             "cs_cart_get_total_cents failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(total_cents == 1000, "Total after adding COFFEE x2 should be 1000.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_cart_add_item_by_id(cart, "COFFEE", 1) == CS_SUCCESS,
             "cs_cart_add_item_by_id COFFEE qty 1 failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_cart_get_total_cents(cart, &total_cents) == CS_SUCCESS,
             "cs_cart_get_total_cents after second add failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(total_cents == 1500, "Total after adding COFFEE x1 should be 1500.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  char* json = nullptr;
  if (!check(cs_cart_get_lines_json(cart, &json) == CS_SUCCESS,
             "cs_cart_get_lines_json failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(json != nullptr && std::strlen(json) > 0,
             "cs_cart_get_lines_json returned empty JSON.")) {
    cs_free(json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(json, "\"total_cents\":1500") != nullptr,
             "JSON missing total_cents 1500.")) {
    cs_free(json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(json, "\"given_cents\":0") != nullptr,
             "JSON missing given_cents 0.")) {
    cs_free(json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(json, "\"change_cents\":0") != nullptr,
             "JSON missing change_cents 0.")) {
    cs_free(json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(json, "\"id\":\"COFFEE\"") != nullptr,
             "JSON missing COFFEE id.")) {
    cs_free(json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(json, "\"name\":\"Coffee\"") != nullptr,
             "JSON missing COFFEE name.")) {
    cs_free(json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(json, "\"line_total_cents\":1500") != nullptr,
             "JSON missing line_total_cents 1500.")) {
    cs_free(json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  cs_free(json);

  if (!check(cs_cart_remove_line(cart, 0) == CS_SUCCESS, "cs_cart_remove_line failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_cart_get_total_cents(cart, &total_cents) == CS_SUCCESS,
             "cs_cart_get_total_cents after remove failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(total_cents == 0, "Total after remove_line should be 0.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_cart_add_item_by_id(cart, "TEA", 1) == CS_SUCCESS,
             "cs_cart_add_item_by_id TEA failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_payment_set_given_cents(cart, 1200) == CS_SUCCESS,
             "cs_payment_set_given_cents 1200 before clear failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_cart_clear(cart) == CS_SUCCESS, "cs_cart_clear failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_cart_get_total_cents(cart, &total_cents) == CS_SUCCESS,
             "cs_cart_get_total_cents after clear failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(total_cents == 0, "Total after clear should be 0.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  char* clear_json = nullptr;
  if (!check(cs_cart_get_lines_json(cart, &clear_json) == CS_SUCCESS,
             "cs_cart_get_lines_json after clear failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(clear_json, "\"given_cents\":0") != nullptr,
             "Clear JSON missing given_cents 0.")) {
    cs_free(clear_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(clear_json, "\"lines\":[]") != nullptr,
             "Clear JSON missing empty lines array.")) {
    cs_free(clear_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  cs_free(clear_json);

  if (!check(cs_cart_add_item_by_id(cart, "TEA", 2) == CS_SUCCESS,
             "cs_cart_add_item_by_id TEA qty 2 failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_payment_set_given_cents(cart, 1000) == CS_SUCCESS,
             "cs_payment_set_given_cents 1000 failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  char* payment_json = nullptr;
  if (!check(cs_cart_get_lines_json(cart, &payment_json) == CS_SUCCESS,
             "cs_cart_get_lines_json for payment scenario failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(payment_json, "\"total_cents\":800") != nullptr,
             "Payment JSON missing total_cents 800.")) {
    cs_free(payment_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(payment_json, "\"given_cents\":1000") != nullptr,
             "Payment JSON missing given_cents 1000.")) {
    cs_free(payment_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(payment_json, "\"change_cents\":200") != nullptr,
             "Payment JSON missing change_cents 200.")) {
    cs_free(payment_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(payment_json, "\"line_total_cents\":800") != nullptr,
             "Payment JSON missing line_total_cents 800.")) {
    cs_free(payment_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  cs_free(payment_json);

  if (!check(cs_cart_clear(cart) == CS_SUCCESS, "cs_cart_clear before raw change failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_cart_add_item_by_id(cart, "TEA", 1) == CS_SUCCESS,
             "cs_cart_add_item_by_id TEA qty 1 for raw change failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_payment_set_given_cents(cart, 100) == CS_SUCCESS,
             "cs_payment_set_given_cents 100 failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  long long raw_change_cents = 0;
  if (!check(cs_payment_get_change_cents(cart, &raw_change_cents) == CS_SUCCESS,
             "cs_payment_get_change_cents for raw change failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(raw_change_cents == -300, "Raw change should be -300 when given is less than total.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  char* raw_change_json = nullptr;
  if (!check(cs_cart_get_lines_json(cart, &raw_change_json) == CS_SUCCESS,
             "cs_cart_get_lines_json for raw change failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strstr(raw_change_json, "\"change_cents\":0") != nullptr,
             "Raw change JSON should clamp change_cents to 0.")) {
    cs_free(raw_change_json);
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  cs_free(raw_change_json);

  if (!check(cs_cart_add_item_by_id(cart, "COFFEE", 0) == CS_ERROR_INVALID_ARGUMENT,
             "cs_cart_add_item_by_id qty 0 should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strlen(cs_last_error()) > 0, "cs_last_error should be set for qty 0.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_cart_add_item_by_id(cart, "UNKNOWN", 1) == CS_ERROR_INVALID_ARGUMENT,
             "cs_cart_add_item_by_id unknown item should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strlen(cs_last_error()) > 0,
             "cs_last_error should be set for unknown item_id.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  cs_cart_free(cart);
  cs_shutdown();
  return 0;
}
