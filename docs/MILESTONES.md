# Milestones

_Last updated: 2026-05-27_

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
- Full persistence rollout and deployment hardening beyond MVP closeout.

## Mobile Event Rollout (Early Jul 2026)
**Target date:** 2026-07-05  
**Status:** Active milestone (as of 2026-05-27)

**Definition**
- Deliver mobile ordering/payment operations, account rollout, and event reporting needed for real usage.

**Current scope (through early Jul 2026)**
- Restaurant/festwirtschaft mode with Android order send to host POS.
- Android payment flow (RFID/NFC + TWINT) and synced payment state.
- Trinkgeld support in payment flow and reporting.
- Account creation from all devices/users.
- Admin-only user controls (role promotion and controlled user management/export).
- History + statistics with showcase mode excluded from both history and aggregation.
- Event mode with multi-register parallel selling and per-register + total event analytics.
- Tutorial/onboarding, startup animation, and selected UI polish.

**Implemented local host slice (2026-05-27)**
- Completed sales can be saved locally with event/register/user metadata, payment method, tip amount, and line counts.
- Recent history and basic statistics exclude showcase sales by default, with an explicit include toggle for review.

**Out of scope**
- Legacy alternative input track from earlier planning.
- Experimental integrations not required for the 2026-07-05 target.
