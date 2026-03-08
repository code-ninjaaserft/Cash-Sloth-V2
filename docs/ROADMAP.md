# Cash-Sloth v2 Roadmap

_Last updated: 2026-03-08_

## Architecture summary
Cash-Sloth v2 is layered as **Core (C++) -> C-API (C ABI) -> WPF (.NET)**. The C++ core owns business rules and data shaping, the C-API exposes a stable boundary (JSON/`char*` with explicit free patterns), and the WPF app focuses on workflow and presentation.

## Always Validate & Update
Every change should be accompanied by a quick validation pass (tests/builds if available) and a documentation check to keep this repo honest about what exists versus what is planned.

## Milestones overview
Dates are planning targets and may be adjusted.

### QEN-GV (Mid-Mar 2026)
**Target date:** 2026-03-14  
Focus: stabilize the shipped MVP workflows for event readiness.

**Phase status (as of 2026-03-06)**
1. **Foundation stability** (`done`)
   - Core DLL + WPF app build reliably on Windows.
   - CI passes on `main` without manual patching.
2. **C-API hardening** (`done`)
   - Catalog, cart, and payment contracts are stable and documented.
   - Contract tests cover baseline happy path and invalid-input paths.
3. **POS workflow readiness** (`done`, pending final manual rehearsal)
   - Product/category interactions and cart/payment flow are implemented in MVP usage.
   - Catalog edit flow is deterministic (cart reset + refresh after catalog updates).
4. **Customer display rehearsal** (`in progress`)
   - Window behavior is implemented and wired; packaged smoke verification is pending.
5. **Release rehearsal** (`in progress`)
   - Tag-based packaging produced release artifact (`v2.0.0`) on 2026-03-06.
   - Packaged-output smoke run notes and final team sign-off are still open.

### Z'Ämme ässe (Aug 2026)
Focus: operational readiness, data persistence, and UI polish.

**Roadmap cadence (toward 2026-08-22)**
1. **2026-03-16 to 2026-03-31: planning freeze**
   - Finalize persistence scope and data ownership boundaries.
   - Write technical design notes for preset model + migration approach.
   - Kickoff progress: SQLite scaffold + migration baseline documented in `docs/AUGUST_PERSISTENCE_KICKOFF.md`.
2. **2026-04-01 to 2026-05-15: presets & persistence**
   - Implement preset model with load/save path.
   - Add SQLite schema versioning with deterministic migrations.
3. **2026-05-16 to 2026-06-20: input readiness**
   - Deliver barcode wedge pipeline and parsing tests.
   - Decide if optional Android-Bluetooth hook remains in or out.
4. **2026-06-21 to 2026-07-20: UI/customer display polish**
   - Dynamic button layout and text fitting for operational catalogs.
   - Customer display readability and refresh smoothing.
5. **2026-07-21 to 2026-08-10: rehearsal + freeze**
   - Execute event rehearsal checklist.
   - Apply bug-fix-only freeze for remaining blockers.
6. **2026-08-11 to 2026-08-22: final buffer**
   - Contingency buffer for blockers and packaging fixes.
   - Final sign-off package for event usage.

**August milestone DoD**
- Presets and persistence are stable, migration-safe, and documented.
- Input handling is test-covered and resilient for event operation.
- UI/customer display polish and rehearsal checklist are completed.

## Related docs
- [Milestones](MILESTONES.md)
- [Repo documentation index](README.md)
