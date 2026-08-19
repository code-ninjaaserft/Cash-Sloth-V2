# Cash-Sloth v2 Roadmap

_Last updated: 2026-08-19_

This roadmap reflects the repository as it exists now. Historical QEN-GV dates and checklists remain available for release evidence, but they are not the active architecture plan.

## Current architecture baseline

- `CashSloth.Core`: native catalog/cart/payment rules behind the documented C ABI.
- `CashSloth.App`: .NET 10 WPF POS with local operational persistence.
- `CashSloth.Contracts`: shared central API v1 records and roles.
- `CashSloth.Server`: Windows WPF control center plus loopback ASP.NET Core host, Cloudflare Tunnel integration, EF Core/SQLite, identity, audit, and backups.
- Central server v1 owns accounts, devices, central presets, exchange rates, and translations. Sales/orders/payment state are explicitly not centralized yet.

## Phase 1: central server v1 integration — implemented

- [x] Central server control application and API v1
- [x] First-admin setup without default credentials
- [x] Trust export/import and fingerprint confirmation
- [x] Per-installation pairing and device proof-of-possession
- [x] Central registration, approval, roles, login, refresh, logout, and password changes
- [x] Authenticated versioned presets and active-preset cache
- [x] Central exchange-rate/translation reference endpoints
- [x] Audit, backups, restore, tunnel lifecycle, and server packaging foundation
- [x] Removal of the local WPF account store and old anonymous preset API/client

## Phase 2: August deployment readiness — active

- [ ] Rehearse a clean install of server and POS on target Windows hardware.
- [ ] Validate Cloudflare tunnel setup, trust/pairing, approval, temporary-password, role-change, device-block, and offline/reconnect flows.
- [ ] Complete Z'Ämme ässe packaging smoke tests and clearly identify the packaged branch/profile.
- [ ] Finish touch/responsive layout and central-flow localization.
- [ ] Verify encrypted backup/restore on the actual migration path and document operator recovery steps.
- [ ] Finalize the certificate, update-manifest, and controlled distribution process.

## Phase 3: shared event operations — design before implementation

- [ ] Decide whether event data lives on the central internet server, a host POS on the LAN, or both.
- [ ] Define authoritative order/sale/payment schemas and idempotent synchronization.
- [ ] Centralize multi-register totals without breaking offline POS operation.
- [ ] Build the selected mobile/PWA ordering workflow and host acceptance flow.
- [ ] Add the chosen TWINT/NFC/provider handoff with auditable payment states and tips.
- [ ] Run multi-device soak and real-event tests.

## Definition of done for the active phase

- A new operator can set up server v1 and pair a clean POS installation using only repository documentation.
- No local account or legacy preset-server path is reachable in the WPF client.
- All managed and native automated checks pass, and the packaged binaries complete the manual smoke workflow.
- Offline boundaries and non-v1 features are stated accurately rather than implied as complete.

## Related documents

- [CSV2 local bucketlist](CSV2_BUCKETLIST.md)
- [Server and external bucketlist](CSV2_SERVER_EXTERNAL_BUCKETLIST.md)
- [Milestones](MILESTONES.md)
- [Central server operations](../src/CashSloth.Server/README.md)
