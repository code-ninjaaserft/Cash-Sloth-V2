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

  cs_cart_t cart = nullptr;
  if (!check(cs_cart_new(&cart) == CS_SUCCESS, "cs_cart_new failed.")) {
    cs_shutdown();
    return 1;
  }

  if (!check(cs_cart_add_item_by_id(cart, "COFFEE", 2) == CS_SUCCESS,
             "cs_cart_add_item_by_id COFFEE qty 2 failed.")) {
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

  long long change_cents = 0;
  if (!check(cs_payment_get_change_cents(cart, &change_cents) == CS_SUCCESS,
             "cs_payment_get_change_cents failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(change_cents == 0, "Change should be 0 when given equals total.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_payment_set_given_cents(cart, 1500) == CS_SUCCESS,
             "cs_payment_set_given_cents 1500 failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_payment_get_change_cents(cart, &change_cents) == CS_SUCCESS,
             "cs_payment_get_change_cents after 1500 failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(change_cents == 500, "Change should be 500 when given exceeds total.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_payment_set_given_cents(cart, 500) == CS_SUCCESS,
             "cs_payment_set_given_cents 500 failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(cs_payment_get_change_cents(cart, &change_cents) == CS_SUCCESS,
             "cs_payment_get_change_cents after 500 failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(change_cents == -500, "Change should be -500 when given is less than total.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_payment_set_given_cents(cart, -1) == CS_ERROR_INVALID_ARGUMENT,
             "cs_payment_set_given_cents negative should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strlen(cs_last_error()) > 0,
             "cs_last_error should be set for negative given_cents.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_payment_set_given_cents(nullptr, 0) == CS_ERROR_INVALID_ARGUMENT,
             "cs_payment_set_given_cents null cart should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strlen(cs_last_error()) > 0,
             "cs_last_error should be set for null cart in set_given.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_payment_get_change_cents(nullptr, &change_cents) == CS_ERROR_INVALID_ARGUMENT,
             "cs_payment_get_change_cents null cart should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strlen(cs_last_error()) > 0,
             "cs_last_error should be set for null cart in get_change.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_payment_get_change_cents(cart, nullptr) == CS_ERROR_INVALID_ARGUMENT,
             "cs_payment_get_change_cents null out should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strlen(cs_last_error()) > 0,
             "cs_last_error should be set for null out_change_cents.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  long long given_cents = 0;
  if (!check(cs_payment_get_given_cents(nullptr, &given_cents) == CS_ERROR_INVALID_ARGUMENT,
             "cs_payment_get_given_cents null cart should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strlen(cs_last_error()) > 0,
             "cs_last_error should be set for null cart in get_given.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_payment_get_given_cents(cart, nullptr) == CS_ERROR_INVALID_ARGUMENT,
             "cs_payment_get_given_cents null out should fail.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(std::strlen(cs_last_error()) > 0,
             "cs_last_error should be set for null out_given_cents.")) {
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

  if (!check(cs_payment_get_given_cents(cart, &given_cents) == CS_SUCCESS,
             "cs_payment_get_given_cents after clear failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(given_cents == 0, "Given cents should reset to 0 after clear.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  if (!check(cs_payment_get_change_cents(cart, &change_cents) == CS_SUCCESS,
             "cs_payment_get_change_cents after clear failed.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }
  if (!check(change_cents == 0, "Change should be 0 after clear with zero given.")) {
    cs_cart_free(cart);
    cs_shutdown();
    return 1;
  }

  cs_cart_free(cart);
  cs_shutdown();
  return 0;
}
