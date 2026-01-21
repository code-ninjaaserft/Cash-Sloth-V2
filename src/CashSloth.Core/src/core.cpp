#include "cashsloth_core.h"

#include <cstdlib>
#include <cstring>
#include <string>

namespace {
thread_local std::string g_last_error;

void set_last_error(const char* message) {
  if (message) {
    g_last_error = message;
  } else {
    g_last_error.clear();
  }
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
