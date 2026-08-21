# CSV2 Server & External Work

_Last updated: 2026-08-21_

This document tracks central-server, multi-device, network, deployment, mobile-client, and third-party work. `CashSloth.CoreApi` is the local native ABI boundary and is not a web service.

## Central server v1: implemented

- `CashSloth.Server` is a Windows WPF control center that hosts ASP.NET Core/Kestrel on loopback and exposes API v1 through a verified Cloudflare Tunnel process.
- First administrator setup has no default credentials or `admin/admin` fallback.
- Central ASP.NET Core Identity accounts support approval, active/block state, lockout, forced temporary-password change, and `User`/`Creator`/`Admin` roles.
- Each CSV2 installation imports a fingerprinted trust document, creates a DPAPI-protected ECDSA device key, and pairs with a short-lived code.
- Authentication uses device proof-of-possession, pinned ES256 access-token validation, 12-hour access tokens, and rotating hashed 30-day refresh tokens.
- Central presets are versioned and protected by role-based API policies with optimistic concurrency.
- Exchange-rate snapshots and translation lookup/cache are served through authenticated reference endpoints.
- Administrators can inspect/manage accounts and devices and query audit events through API v1; the server UI also provides emergency local administration.
- Server data uses EF Core/SQLite with committed migrations, WAL, backups, and encrypted migration backup/restore.
- Tunnel tokens and signing keys are DPAPI protected; the tunnel token is passed to `cloudflared` through the environment rather than command-line arguments.
- The WPF POS consumes the authenticated central account, preset, and exchange-rate paths. The old `CashSloth.PresetApi` and anonymous client are retired.
- Its Accounts and Presets workspaces now expose only the surfaces allowed by session state and the server-provided `User`/`Creator`/`Admin` role.
- Automated tests cover account policy, pairing/challenges, cryptography, preset concurrency, reference data, backups, and the HTTP authorization matrix.

## Central server V1.5 events: implemented

- Events are created as drafts by Creator/Admin accounts, use immutable central-preset snapshots and freeze their rules when published.
- Event roles are `Host` and `Participant`; every member chooses a unique event nickname. The host can rename participants, kick them permanently, close and finalise the event.
- Open events and code-protected events are visible to paired signed-in clients. The host can resume control on another paired device.
- Completed sales use idempotent HTTP batches and a local atomic SQLite outbox. SignalR only provides realtime change hints; polling remains the fallback.
- A pinned ES256 offline lease permits up to twelve hours of event checkout while disconnected. Closing immediately freezes new checkout and creates a fixed cutoff.
- Server statistics provide overall and per-member totals; host reports additionally contain item/payment breakdowns and full sale data for CSV/PNG export.
- The old UDP/LAN event surface is no longer presented in CSV2. Mobile orders and provider payment state remain separate future work.

See [CashSloth.Server README](../src/CashSloth.Server/README.md) for operation and packaging details.

## Central server v1: validation and polish still open

- Complete a clean-machine rehearsal for server setup, Cloudflare configuration, trust export, client pairing, account approval, backup, restore, and update checks.
- Confirm the MSIX certificate/distribution process and document the production update-manifest location.
- Exercise tunnel interruption, server restart, token expiry, device blocking, role changes, and client recovery on real event hardware.
- Finish localization and UX polish in both WPF applications.
- Add operational monitoring/alerting appropriate to the final deployment environment.

## Open: mobile ordering

- Choose and build the customer-facing Android, web, or PWA client.
- Define the server-side order model, ownership, stable IDs, idempotency, retry, reconnect, expiry, and audit behavior.
- Add the host workflow to receive, display, accept/reject, and process incoming orders.
- Return useful order state to the customer device.
- Define offline/event-network behavior before choosing transport technology.

## Open: payment and tips

- Decide whether TWINT means provider integration, payment link/deep link, QR handoff, or manual confirmation.
- Verify provider contracts, fees, credentials, test/production environments, callbacks/webhooks, and reconciliation requirements.
- Verify whether phone NFC/RFID can legally and technically serve the intended payment flow; tag reading alone is not card processing.
- Model pending/success/failed/cancelled results and synchronize them idempotently.
- Include card/mobile tips in payment results and reporting.

## Event validation still open

- Rehearse repeated real-event use with multiple tills, tunnel interruption, host-device takeover and incomplete finalisation.
- Decide retention/privacy policy for the permanent server-side event history.

## Architecture decisions before v2 server scope

- Should mobile/event traffic use the existing internet-facing central server, a host-POS LAN service, or a hybrid?
- Which workflows must function with no internet connection?
- Is automatic payment processing required, or is a provider handoff plus operator confirmation sufficient?
- What retention, privacy, export, and audit requirements apply to accounts, orders, and sales?
