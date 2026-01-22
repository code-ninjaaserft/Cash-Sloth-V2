#include "cashsloth_core.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <new>
#include <string>
#include <vector>

namespace {
thread_local std::string g_last_error;

void set_last_error(const char* message) {
  if (message) {
    g_last_error = message;
  } else {
    g_last_error.clear();
  }
}

struct CartLine {
  std::string item_id;
  int qty = 0;
  long long unit_cents = 0;
};

class Cart {
 public:
  std::vector<CartLine> lines;
  long long given_cents = 0;
};

bool lookup_unit_cents(const std::string& item_id, long long* out_unit_cents) {
  if (!out_unit_cents) {
    return false;
  }

  if (item_id == "COFFEE") {
    *out_unit_cents = 500;
    return true;
  }
  if (item_id == "TEA") {
    *out_unit_cents = 400;
    return true;
  }
  if (item_id == "WATER") {
    *out_unit_cents = 300;
    return true;
  }

  return false;
}

Cart* as_cart(cs_cart_t cart) {
  return static_cast<Cart*>(cart);
}

long long compute_total_cents(const Cart& cart) {
  long long total = 0;
  for (const auto& line : cart.lines) {
    total += line.unit_cents * static_cast<long long>(line.qty);
  }
  return total;
}

std::string escape_json_string(const std::string& input) {
  std::string output;
  output.reserve(input.size());
  for (unsigned char ch : input) {
    switch (ch) {
      case '\"':
        output += "\\\"";
        break;
      case '\\':
        output += "\\\\";
        break;
      case '\b':
        output += "\\b";
        break;
      case '\f':
        output += "\\f";
        break;
      case '\n':
        output += "\\n";
        break;
      case '\r':
        output += "\\r";
        break;
      case '\t':
        output += "\\t";
        break;
      default:
        if (ch < 0x20) {
          char buffer[7];
          std::snprintf(buffer, sizeof(buffer), "\\u%04x", ch);
          output += buffer;
        } else {
          output += static_cast<char>(ch);
        }
        break;
    }
  }
  return output;
}
}  // namespace

int cs_init() {
  set_last_error(nullptr);
  return CS_SUCCESS;
}

void cs_shutdown() {
  set_last_error(nullptr);
}

const char* cs_last_error() {
  return g_last_error.c_str();
}

void cs_free(void* p) {
  std::free(p);
}

int cs_get_version(char** out_json) {
  if (!out_json) {
    set_last_error("out_json must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  constexpr const char* kVersionJson = "{\"version\":\"0.1.0\"}";
  const size_t size = std::strlen(kVersionJson) + 1;
  char* buffer = static_cast<char*>(std::malloc(size));
  if (!buffer) {
    set_last_error("Out of memory allocating version JSON.");
    return CS_ERROR_OUT_OF_MEMORY;
  }

  std::memcpy(buffer, kVersionJson, size);
  *out_json = buffer;
  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_cart_new(cs_cart_t* out_cart) {
  if (!out_cart) {
    set_last_error("out_cart must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  Cart* cart = new (std::nothrow) Cart();
  if (!cart) {
    set_last_error("Out of memory allocating cart.");
    return CS_ERROR_OUT_OF_MEMORY;
  }

  *out_cart = cart;
  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_cart_free(cs_cart_t cart) {
  if (!cart) {
    set_last_error(nullptr);
    return CS_SUCCESS;
  }

  delete as_cart(cart);
  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_cart_clear(cs_cart_t cart) {
  Cart* cart_ptr = as_cart(cart);
  if (!cart_ptr) {
    set_last_error("cart must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  cart_ptr->lines.clear();
  cart_ptr->given_cents = 0;
  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_cart_add_item_by_id(cs_cart_t cart, const char* item_id, int qty) {
  Cart* cart_ptr = as_cart(cart);
  if (!cart_ptr) {
    set_last_error("cart must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }
  if (!item_id || item_id[0] == '\0') {
    set_last_error("item_id must not be null or empty.");
    return CS_ERROR_INVALID_ARGUMENT;
  }
  if (qty <= 0) {
    set_last_error("qty must be greater than zero.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  std::string item_id_str(item_id);
  long long unit_cents = 0;
  if (!lookup_unit_cents(item_id_str, &unit_cents)) {
    g_last_error = "Unknown item_id: " + item_id_str;
    return CS_ERROR_INVALID_ARGUMENT;
  }

  for (auto& line : cart_ptr->lines) {
    if (line.item_id == item_id_str) {
      line.qty += qty;
      set_last_error(nullptr);
      return CS_SUCCESS;
    }
  }

  cart_ptr->lines.push_back(CartLine{item_id_str, qty, unit_cents});
  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_cart_remove_line(cs_cart_t cart, int line_index) {
  Cart* cart_ptr = as_cart(cart);
  if (!cart_ptr) {
    set_last_error("cart must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }
  if (line_index < 0 || static_cast<size_t>(line_index) >= cart_ptr->lines.size()) {
    set_last_error("line_index out of range.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  cart_ptr->lines.erase(cart_ptr->lines.begin() + line_index);
  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_cart_get_total_cents(cs_cart_t cart, long long* out_total_cents) {
  Cart* cart_ptr = as_cart(cart);
  if (!cart_ptr) {
    set_last_error("cart must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }
  if (!out_total_cents) {
    set_last_error("out_total_cents must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  *out_total_cents = compute_total_cents(*cart_ptr);
  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_cart_get_lines_json(cs_cart_t cart, char** out_json) {
  Cart* cart_ptr = as_cart(cart);
  if (!cart_ptr) {
    set_last_error("cart must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }
  if (!out_json) {
    set_last_error("out_json must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  long long total = 0;
  std::string json;
  json.reserve(128);
  json += "{\"lines\":[";
  for (size_t i = 0; i < cart_ptr->lines.size(); ++i) {
    const auto& line = cart_ptr->lines[i];
    const long long line_cents = line.unit_cents * static_cast<long long>(line.qty);
    total += line_cents;
    if (i > 0) {
      json += ",";
    }
    json += "{\"item_id\":\"";
    json += escape_json_string(line.item_id);
    json += "\",\"qty\":";
    json += std::to_string(line.qty);
    json += ",\"unit_cents\":";
    json += std::to_string(line.unit_cents);
    json += ",\"line_cents\":";
    json += std::to_string(line_cents);
    json += "}";
  }
  json += "],\"total_cents\":";
  json += std::to_string(total);
  json += "}";

  const size_t size = json.size() + 1;
  char* buffer = static_cast<char*>(std::malloc(size));
  if (!buffer) {
    set_last_error("Out of memory allocating cart JSON.");
    return CS_ERROR_OUT_OF_MEMORY;
  }

  std::memcpy(buffer, json.c_str(), size);
  *out_json = buffer;
  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_payment_set_given_cents(cs_cart_t cart, long long given_cents) {
  Cart* cart_ptr = as_cart(cart);
  if (!cart_ptr) {
    set_last_error("cart must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }
  if (given_cents < 0) {
    set_last_error("given_cents must be non-negative.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  cart_ptr->given_cents = given_cents;
  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_payment_get_change_cents(cs_cart_t cart, long long* out_change_cents) {
  Cart* cart_ptr = as_cart(cart);
  if (!cart_ptr) {
    set_last_error("cart must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }
  if (!out_change_cents) {
    set_last_error("out_change_cents must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  const long long total = compute_total_cents(*cart_ptr);
  *out_change_cents = cart_ptr->given_cents - total;
  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_payment_get_given_cents(cs_cart_t cart, long long* out_given_cents) {
  Cart* cart_ptr = as_cart(cart);
  if (!cart_ptr) {
    set_last_error("cart must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }
  if (!out_given_cents) {
    set_last_error("out_given_cents must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  *out_given_cents = cart_ptr->given_cents;
  set_last_error(nullptr);
  return CS_SUCCESS;
}
