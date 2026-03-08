# Issue B — Core demo catalog strategy

**Suggested labels:** `core`, `catalog`, `planning`
**Status (2026-03-06):** Completed

## Description
Define the lightweight demo catalog strategy for the MVP (JSON shape, load flow, and how the WPF shell feeds it into the core).

## Definition of Done
- A short strategy note exists and aligns with the current core catalog load/get behavior.
- The plan avoids new storage or barcode features.

## Task checklist
- [x] Document the MVP catalog JSON fields and example payload.
- [x] Describe how and when the WPF app loads catalog JSON into the core.
- [x] Call out any constraints (id uniqueness, unit_cents rules) already enforced by the core.

## Evidence
- Catalog JSON shape and constraints: `docs/ABI.md` (`Catalog JSON format`, `Catalog functions`).
- WPF load flow: `src/CashSloth.App/MainWindow.xaml.cs` (`LoadCatalogIntoCore`).
