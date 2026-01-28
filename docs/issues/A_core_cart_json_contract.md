# Issue A — Core cart JSON contract (POS-ready)

**Suggested labels:** `core`, `contract`, `mvp`

## Description
Upgrade `cs_cart_get_lines_json` to emit a POS-ready payload with catalog names and payment fields, without changing the C ABI or allocation pattern.

## Definition of Done
- `cs_cart_get_lines_json` returns `lines`, `total_cents`, `given_cents`, and `change_cents` for empty and non-empty carts.
- Line objects include `id`, `name`, `unit_cents`, `qty`, `line_total_cents`.
- Contract tests are updated and passing.

## Task checklist
- [ ] Implement the updated JSON schema in `core.cpp`.
- [ ] Look up catalog names by id (empty string when missing).
- [ ] Update contract tests to assert the new fields and add empty/payment scenarios.
- [ ] Keep `malloc` + `cs_free` allocation behavior.
