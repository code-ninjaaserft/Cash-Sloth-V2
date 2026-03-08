# Issue C — WPF POS MainWindow MVP

**Suggested labels:** `wpf`, `ui`, `mvp`
**Status (2026-03-06):** Completed

## Description
Describe the MVP MainWindow UI layout and the core API calls used for cart and payment actions.

## Definition of Done
- A concise MainWindow MVP spec is documented (layout + interaction flow).
- The spec maps UI actions to existing core C-API calls.

## Task checklist
- [x] List required UI areas (cart lines, totals, tender input, actions).
- [x] Map add/remove/clear/tender actions to current C-API calls.
- [x] Note any formatting expectations (currency display, qty updates).

## Implementation note
- Main areas: product/category selectors, cart lines, totals, tender controls, and status/error text.
- C-API mapping:
  - add line: `cs_cart_add_item_by_id`
  - remove line: `cs_cart_remove_line`
  - clear cart: `cs_cart_clear`
  - tender/reset: `cs_payment_set_given_cents`
  - snapshot refresh: `cs_cart_get_lines_json`
- Formatting: CHF values are rendered from cents with two decimal places in UI and customer display.
