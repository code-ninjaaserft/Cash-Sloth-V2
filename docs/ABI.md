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
- `cs_catalog_get_json(char** out_json)` allocates and returns JSON that must be released via `cs_free`.

## Catalog functions
- `cs_catalog_load_json(const char* json)` replaces the process-wide catalog with the provided JSON.
  - JSON must be non-null, non-empty, and parseable.
  - Each item requires a non-empty `id` and `unit_cents >= 0`. Duplicate `id` values are rejected.
  - On success, the catalog is replaced atomically; on failure, the existing catalog remains unchanged.
- `cs_catalog_get_json(char** out_json)` returns the current catalog JSON in the same format used for loading.
  - The response always includes a `name` field (empty string when not set).

## Catalog JSON format
Catalog JSON uses this MVP format (no pretty printing required):
```json
{"items":[{"id":"COFFEE","name":"Coffee","unit_cents":500},{"id":"TEA","name":"Tea","unit_cents":400}]}
```

## Cart handles and functions
- `cs_cart_t` is an opaque handle representing a cart instance.
- Use `cs_cart_new(cs_cart_t* out_cart)` to allocate a new cart.
- Use `cs_cart_free(cs_cart_t cart)` to release a cart. Passing `nullptr` is a no-op and returns success.
- `cs_cart_clear(cs_cart_t cart)` removes all lines from the cart.
- `cs_cart_add_item_by_id(cs_cart_t cart, const char* item_id, int qty)` adds quantity for a known item.
  - If the item already exists in the cart, quantity is increased.
  - `item_id` must be non-null and non-empty; `qty` must be greater than zero.
  - If `item_id` is unknown in the current catalog, returns `CS_ERROR_INVALID_ARGUMENT`.
- `cs_cart_remove_line(cs_cart_t cart, int line_index)` removes a line by 0-based index.
- `cs_cart_get_total_cents(cs_cart_t cart, long long* out_total_cents)` returns the current total in cents.
- `cs_cart_get_lines_json(cs_cart_t cart, char** out_json)` returns a JSON summary; callers must free the
  returned buffer via `cs_free`.
- `cs_cart_clear(cs_cart_t cart)` resets the cart lines and resets any payment `given_cents` to 0.
- Cart lines are not retroactively adjusted if the catalog is reloaded; existing lines keep their stored
  `unit_cents` values (MVP behavior).

## Payment functions
- `cs_payment_set_given_cents(cs_cart_t cart, long long given_cents)` stores the amount tendered in cents.
  - `given_cents` must be `>= 0`; otherwise returns `CS_ERROR_INVALID_ARGUMENT`.
- `cs_payment_get_given_cents(cs_cart_t cart, long long* out_given_cents)` returns the last stored tendered
  amount in cents.
- `cs_payment_get_change_cents(cs_cart_t cart, long long* out_change_cents)` returns `given_cents - total_cents`.
  - The change value can be negative when the tendered amount is insufficient.

## Cart JSON format
`cs_cart_get_lines_json` returns a UTF-8 JSON object in this format (no pretty printing):
```json
{"lines":[{"id":"COFFEE","name":"Coffee","unit_cents":500,"qty":2,"line_total_cents":1000}],"total_cents":1000,"given_cents":0,"change_cents":0}
```
`lines` are ordered in insertion order, `total_cents` is the sum of `line_total_cents`, and
`change_cents` is clamped at zero (the raw change is still available via
`cs_payment_get_change_cents`).
