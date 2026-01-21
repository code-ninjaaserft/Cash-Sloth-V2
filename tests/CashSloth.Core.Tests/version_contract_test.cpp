#include "cashsloth_core.h"

#include <cstring>
#include <iostream>

int main() {
  if (cs_init() != CS_SUCCESS) {
    std::cerr << "cs_init failed: " << cs_last_error() << "\n";
    return 1;
  }

  char* json = nullptr;
  const int result = cs_get_version(&json);
  if (result != CS_SUCCESS) {
    std::cerr << "cs_get_version failed: " << cs_last_error() << "\n";
    cs_shutdown();
    return 1;
  }

  if (!json || std::strlen(json) == 0) {
    std::cerr << "cs_get_version returned empty JSON.\n";
    cs_free(json);
    cs_shutdown();
    return 1;
  }

  cs_free(json);
  cs_shutdown();
  return 0;
}
