#include "cashsloth_core.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <mutex>
#include <new>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include "mini_json.hpp"

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

struct CatalogItem {
  std::string id;
  std::string name;
  long long unit_cents = 0;
};

struct CatalogState {
  std::vector<CatalogItem> items;
  std::unordered_map<std::string, size_t> index_by_id;
};

std::mutex g_catalog_mutex;
CatalogState g_catalog;

bool lookup_unit_cents(const std::string& item_id, long long* out_unit_cents) {
  if (!out_unit_cents) {
    return false;
  }

  std::lock_guard<std::mutex> lock(g_catalog_mutex);
  auto it = g_catalog.index_by_id.find(item_id);
  if (it == g_catalog.index_by_id.end()) {
    return false;
  }
  *out_unit_cents = g_catalog.items[it->second].unit_cents;
  return true;
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

bool parse_catalog_json(const char* json, CatalogState* out_state, std::string* out_error) {
  if (!json || json[0] == '\0') {
    if (out_error) {
      *out_error = "Catalog JSON must not be null or empty.";
    }
    return false;
  }
  if (!out_state) {
    if (out_error) {
      *out_error = "Catalog output state must not be null.";
    }
    return false;
  }

  mini_json::Value root;
  std::string parse_error;
  if (!mini_json::parse(json, &root, &parse_error)) {
    if (out_error) {
      *out_error = "Invalid catalog JSON: " + parse_error;
    }
    return false;
  }

  if (!root.is_object()) {
    if (out_error) {
      *out_error = "Catalog JSON must be an object.";
    }
    return false;
  }

  const auto& root_obj = root.as_object();
  auto items_it = root_obj.find("items");
  if (items_it == root_obj.end() || !items_it->second.is_array()) {
    if (out_error) {
      *out_error = "Catalog JSON must include an items array.";
    }
    return false;
  }

  CatalogState new_state;
  std::unordered_set<std::string> seen_ids;
  const auto& items = items_it->second.as_array();
  new_state.items.reserve(items.size());

  for (const auto& item_value : items) {
    if (!item_value.is_object()) {
      if (out_error) {
        *out_error = "Catalog items must be JSON objects.";
      }
      return false;
    }
    const auto& item_obj = item_value.as_object();
    auto id_it = item_obj.find("id");
    if (id_it == item_obj.end() || !id_it->second.is_string()) {
      if (out_error) {
        *out_error = "Catalog item id must be a string.";
      }
      return false;
    }
    const std::string& id = id_it->second.as_string();
    if (id.empty()) {
      if (out_error) {
        *out_error = "Catalog item id must not be empty.";
      }
      return false;
    }
    if (!seen_ids.insert(id).second) {
      if (out_error) {
        *out_error = "Duplicate catalog item id: " + id;
      }
      return false;
    }

    auto unit_it = item_obj.find("unit_cents");
    if (unit_it == item_obj.end() || !unit_it->second.is_number() ||
        !unit_it->second.number_is_integer()) {
      if (out_error) {
        *out_error = "Catalog item unit_cents must be an integer.";
      }
      return false;
    }
    long double unit_value = unit_it->second.as_number();
    if (unit_value < 0 ||
        unit_value > static_cast<long double>(std::numeric_limits<long long>::max())) {
      if (out_error) {
        *out_error = "Catalog item unit_cents must be non-negative.";
      }
      return false;
    }

    std::string name;
    auto name_it = item_obj.find("name");
    if (name_it != item_obj.end()) {
      if (!name_it->second.is_string()) {
        if (out_error) {
          *out_error = "Catalog item name must be a string.";
        }
        return false;
      }
      name = name_it->second.as_string();
    }

    CatalogItem item{id, name, static_cast<long long>(unit_value)};
    new_state.index_by_id[id] = new_state.items.size();
    new_state.items.push_back(std::move(item));
  }

  *out_state = std::move(new_state);
  return true;
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

int cs_catalog_load_json(const char* json) {
  CatalogState new_state;
  std::string error;
  if (!parse_catalog_json(json, &new_state, &error)) {
    set_last_error(error.c_str());
    return CS_ERROR_INVALID_ARGUMENT;
  }

  {
    std::lock_guard<std::mutex> lock(g_catalog_mutex);
    g_catalog = std::move(new_state);
  }

  set_last_error(nullptr);
  return CS_SUCCESS;
}

int cs_catalog_get_json(char** out_json) {
  if (!out_json) {
    set_last_error("out_json must not be null.");
    return CS_ERROR_INVALID_ARGUMENT;
  }

  std::vector<CatalogItem> items_snapshot;
  {
    std::lock_guard<std::mutex> lock(g_catalog_mutex);
    items_snapshot = g_catalog.items;
  }

  std::string json;
  json.reserve(128);
  json += "{\"items\":[";
  for (size_t i = 0; i < items_snapshot.size(); ++i) {
    const auto& item = items_snapshot[i];
    if (i > 0) {
      json += ",";
    }
    json += "{\"id\":\"";
    json += escape_json_string(item.id);
    json += "\",\"name\":\"";
    json += escape_json_string(item.name);
    json += "\",\"unit_cents\":";
    json += std::to_string(item.unit_cents);
    json += "}";
  }
  json += "]}";

  const size_t size = json.size() + 1;
  char* buffer = static_cast<char*>(std::malloc(size));
  if (!buffer) {
    set_last_error("Out of memory allocating catalog JSON.");
    return CS_ERROR_OUT_OF_MEMORY;
  }

  std::memcpy(buffer, json.c_str(), size);
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
