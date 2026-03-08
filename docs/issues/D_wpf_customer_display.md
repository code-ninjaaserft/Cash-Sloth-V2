# Issue D — WPF customer display MVP

**Suggested labels:** `wpf`, `ui`, `mvp`
**Status (2026-03-06):** Completed

## Description
Define the MVP customer-facing display window that mirrors cart totals using the existing core JSON contract.

## Definition of Done
- A short spec describes the customer display layout and refresh approach.
- The spec uses the existing core cart JSON payload (no new APIs).

## Task checklist
- [x] Outline the layout (items, total, tendered, change).
- [x] Decide on polling/refresh cadence with current core APIs.
- [x] Note formatting rules for cents-to-currency display.

## Implementation note
- Layout: item lines + summary fields (total, tendered, return/change).
- Refresh cadence: no polling timer; display updates whenever the main POS snapshot refreshes.
- Formatting: cents are formatted as CHF strings with two decimal places.
