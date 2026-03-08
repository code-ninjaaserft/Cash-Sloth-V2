# Issue A — Core cart JSON contract (POS-ready)

**Suggested labels:** `core`, `contract`, `mvp`
**Status (2026-03-06):** Completed

## Description
Upgrade `cs_cart_get_lines_json` to emit a POS-ready payload with catalog names and payment fields, without changing the C ABI or allocation pattern.

## Definition of Done
- `cs_cart_get_lines_json` returns `lines`, `total_cents`, `given_cents`, and `change_cents` for empty and non-empty carts.
- Line objects include `id`, `name`, `unit_cents`, `qty`, `line_total_cents`.
- Contract tests are updated and passing.

## Task checklist
- [x] Implement the updated JSON schema in `core.cpp`.
- [x] Look up catalog names by id (empty string when missing).
- [x] Update contract tests to assert the new fields and add empty/payment scenarios.
- [x] Keep `malloc` + `cs_free` allocation behavior.

## Evidence
- Core JSON contract implementation: `src/CashSloth.Core/src/core.cpp` (`cs_cart_get_lines_json`).
- Contract validation: `tests/CashSloth.Core.Tests/cart_contract_test.cpp` and `tests/CashSloth.Core.Tests/payment_contract_test.cpp`.
