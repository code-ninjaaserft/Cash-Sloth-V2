# Cash-Sloth v2 Roadmap

_Last updated: 2026-05-27_

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

### Mobile Event Rollout (Early Jul 2026)
**Target date:** 2026-07-05  
Focus: complete mobile event operations (ordering + payment), user/account rollout, and reporting polish.

**Roadmap cadence (toward 2026-07-05)**
1. **2026-05-27 to 2026-06-05: mobile ordering host flow**
   - Restaurant/festwirtschaft mode with Android order submission.
   - Host device intake and order processing workflow.
2. **2026-06-06 to 2026-06-12: payment + tip support**
   - Android payment flow with RFID/NFC + TWINT sync to host POS.
   - Tip handling in checkout/statistics model.
3. **2026-06-13 to 2026-06-19: accounts on all devices**
   - Self-service account creation on any device.
   - Admin-only user controls (role promotion and controlled user management).
4. **2026-06-20 to 2026-06-26: history/statistics + showcase boundaries**
   - History and statistics for completed real sales.
   - Showcase mode excluded from history and statistics.
   - Implemented local WPF host slice on 2026-05-27: completed-sale SQLite history, event/register/user metadata, tip totals, and showcase exclusion by default.
5. **2026-06-27 to 2026-07-02: event multi-register analytics**
   - Event mode with parallel tills/users (for example Kasse 1 / Kasse 2).
   - Event analytics by register/user and aggregated event totals.
6. **2026-07-03 to 2026-07-05: UX finish**
   - Tutorial/onboarding flow.
   - Startup animation and targeted UI polish (including window icon behavior on Windows).

**Milestone DoD (2026-07-05)**
- Android app scope is order send + payment to host POS.
- No parallel legacy input track remains in active planning.
- Tip/account/event/statistics features are integrated and documented.

## Related docs
- [Milestones](MILESTONES.md)
- [Repo documentation index](README.md)
