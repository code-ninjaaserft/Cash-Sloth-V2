# Issue D — WPF customer display MVP

**Suggested labels:** `wpf`, `ui`, `mvp`

## Description
Define the MVP customer-facing display window that mirrors cart totals using the existing core JSON contract.

## Definition of Done
- A short spec describes the customer display layout and refresh approach.
- The spec uses the existing core cart JSON payload (no new APIs).

## Task checklist
- [ ] Outline the layout (items, total, tendered, change).
- [ ] Decide on polling/refresh cadence with current core APIs.
- [ ] Note formatting rules for cents-to-currency display.
