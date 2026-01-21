# ABI Contract (CashSloth.Core C-API)

This document defines the stable ABI contract between the native core and external callers.

## Error codes
- `CS_SUCCESS` (0): success.
- `CS_ERROR_INVALID_ARGUMENT` (1): invalid argument passed to API.
- `CS_ERROR_OUT_OF_MEMORY` (2): allocation failure inside the core.
- `CS_ERROR_INTERNAL` (100): unspecified internal error.

All C-API functions return an `int` error code. Any non-zero return indicates failure and sets a
human-readable error message retrievable via `cs_last_error()`.

## Error strings (`cs_last_error`)
- `cs_last_error()` returns a UTF-8 string pointer owned by the core.
- The pointer is valid until the next core call on the same thread.
- Callers must **not** free this pointer.

## Memory ownership (`cs_free`)
- Any `char*` returned by the core is allocated with `malloc`.
- Callers **must** release returned buffers by calling `cs_free(void* p)`.

## JSON boundary
- JSON results are UTF-8 strings (e.g., `{"version":"0.1.0"}`).
- `cs_get_version(char** out_json)` allocates and returns JSON that must be released via `cs_free`.
