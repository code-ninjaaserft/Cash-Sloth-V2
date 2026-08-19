# Milestones

_Last updated: 2026-08-19_

Dates are planning targets; the status below distinguishes historical release evidence from active work.

## QEN-GV MVP — historical

**Original target:** 2026-03-14

**Status:** Core MVP and release artifact were produced; packaged-output sign-off items remain recorded in the historical checklists.

Delivered foundation:

- Native C ABI for catalog, cart, and payment
- WPF shop/cart/customer-display/edit flows
- Baseline native and managed tests
- Tag-driven Windows release artifact

Evidence: [MVP acceptance checklist](QEN_GV_MVP_ACCEPTANCE_CHECKLIST.md) and [release rehearsal checklist](QEN_GV_RELEASE_REHEARSAL_CHECKLIST.md).

## Mobile Event Rollout — superseded scope

**Original target:** 2026-07-05

**Status:** Local history, tips metadata, showcase filtering, discovery, and account/preset foundations landed, but mobile ordering, provider payment, and cross-register synchronization did not. Those unfinished items moved to the [server/external bucketlist](CSV2_SERVER_EXTERNAL_BUCKETLIST.md).

## Central Server v1 — implemented, deployment validation active

**Implementation date:** 2026-08-19

**Status:** Code and automated coverage are present; clean-machine and real-event operational rehearsal remains open.

Delivered:

- Windows server control center, API v1, tunnel lifecycle, and packaging foundation
- Central Identity accounts, role/approval policy, paired devices, tokens, and forced password changes
- Versioned central presets, reference data, audit, and encrypted backup/restore
- WPF client trust/pairing/auth/admin/preset/reference integration
- Removal of old local-account and anonymous preset-server paths

## Z'Ämme ässe / August deployment readiness — active

**Target:** 2026-08-22

**Status:** Final hardware/package/operations rehearsal and UI polish are pending.

Exit criteria:

- Clean server and POS setup succeeds on target machines.
- Trust, pairing, approval, password reset/change, central preset, and reconnect workflows pass.
- Packaged output completes the cash-sale, customer-display, history, and shutdown smoke test.
- Backup/restore and operator recovery steps are verified and documented.
- The release clearly states its branch/profile and server v1 boundaries.
