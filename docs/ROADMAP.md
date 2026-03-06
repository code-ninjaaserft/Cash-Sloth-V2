# Cash-Sloth v2 Roadmap

_Last updated: 2026-03-06_

## Architecture summary
Cash-Sloth v2 is layered as **Core (C++) -> C-API (C ABI) -> WPF (.NET)**. The C++ core owns business rules and data shaping, the C-API exposes a stable boundary (JSON/`char*` with explicit free patterns), and the WPF app focuses on workflow and presentation.

## Always Validate & Update
Every change should be accompanied by a quick validation pass (tests/builds if available) and a documentation check to keep this repo honest about what exists versus what is planned.

## Milestones overview
Dates are planning targets and may be adjusted.

### QEN-GV (Mid-Mar 2026)
**Target date:** 2026-03-14  
Focus: stabilize the shipped MVP workflows for event readiness.

**Phases & DoD**
1. **Foundation stability**
   - Core DLL + WPF app build reliably on Windows.
   - CI passes without manual patching.
2. **C-API hardening**
   - Catalog, cart, and payment contracts remain stable and documented.
   - Contract tests cover happy path and key invalid-input paths.
3. **POS workflow readiness**
   - Product/category interactions and cart/payment flow are stable in MVP usage.
   - Catalog edit flow remains deterministic (cart reset + clean refresh behavior).
4. **Customer display rehearsal**
   - Second-screen behavior verified in single-screen fallback and dual-screen mode.
5. **Release rehearsal**
   - Tag-based packaging tested once end-to-end (best effort).

### Z'Ämme ässe (Aug 2026)
Focus: operational readiness, data persistence, and UI polish.

**Phases & DoD**
1. **Presets & persistence**
   - Preset model defined with load/save pipeline.
   - SQLite persistence with schema versioning and migration strategy.
2. **Input & device readiness**
   - Barcode wedge pipeline with parsing tests.
   - Optional Android-Bluetooth protocol spec + prototype hook documented.
3. **UI & customer display polish**
   - Dynamic button layout + text fitting.
   - Customer display refinements and rehearsal checklist.

## Related docs
- [Milestones](MILESTONES.md)
- [Repo documentation index](README.md)
