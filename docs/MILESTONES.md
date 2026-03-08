# Milestones

_Last updated: 2026-03-08_

Dates are planning targets and can be adjusted as scope is refined.

## QEN-GV (Mid-Mar 2026)
**Target date:** 2026-03-14  
**Status:** In progress, final rehearsal sign-off pending (as of 2026-03-06)

**Definition**
- Deliver a stable MVP workflow for event rehearsal on Windows.
- Keep C-API contracts (catalog/cart/payment) stable and covered by baseline tests.

**Current verified status (2026-03-06)**
- [x] Local app release build succeeds (`dotnet restore` + `dotnet build -c Release`).
- [x] Core contract tests pass (`ctest` 4/4).
- [x] CI on `main` is green (run `22778999078`, 2026-03-06).
- [x] Release workflow produced ZIP artifact (`cash-sloth-v2-v2.0.0-windows.zip`, run `22779133037`).
- [ ] Packaged-output smoke run and team sign-off are still open.

**Out of scope**
- Full persistence rollout, barcode workflows, and deployment hardening.

## Z'Ämme ässe (Aug 2026)
**Target date:** 2026-08-22  
**Status:** In progress (kickoff started, as of 2026-03-08)

**Definition**
- Deliver persistence, presets, input pipeline, and UI polish needed for operations.

**Planning baseline (Mar -> Aug 2026)**
- Presets + persistence (schema, load/save, migration strategy).
- Input readiness (barcode wedge path + tests).
- UI and customer display polish, then rehearsal/freeze checklist.
- Kickoff completed: SQLite persistence scaffolding + JSON fallback bridge (`docs/AUGUST_PERSISTENCE_KICKOFF.md`).

**Out of scope**
- Experimental integrations that are not required for event readiness.
