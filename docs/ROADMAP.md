# Cash-Sloth v2 Roadmap

## Architecture summary
Cash-Sloth v2 is layered as **Core (C++) → C-API (C ABI) → WPF (.NET)**. The C++ core owns business rules and data shaping, the C-API exposes a stable boundary (JSON/`char*` with explicit free patterns), and the WPF app focuses on workflow and presentation. This roadmap describes planned work only; it does not imply implementation has started.

## Always Validate & Update
Every change should be accompanied by a quick validation pass (tests/builds if available) and a documentation check to keep this repo honest about what exists versus what is planned.

## Milestones overview
Dates are planning targets and may be adjusted.

### QEN-GV (Feb 2026)
Focus: buildable skeleton and core MVP behaviors with a thin WPF proof-of-life.

**Phases & DoD**
1. **Foundation scaffolding**
   - CMake config builds a core DLL.
   - WPF app stub builds (even if UI is minimal).
   - CI can run on Windows without failing when projects/tests are missing.
2. **C-API contract**
   - Initial C-API surface defined with JSON/memory conventions.
   - Error-handling strategy documented.
3. **Core MVP**
   - Cart + payment MVP functions with unit tests (ctest integration).
   - Catalog loads from JSON in core.
4. **UI proof-of-life**
   - WPF buttons call into C-API and refresh the UI.
   - Customer display window stubbed for second-screen support.
5. **Release scaffolding**
   - Tag-based release workflow packages artifacts (best-effort).

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
