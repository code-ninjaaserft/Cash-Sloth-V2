# CashSloth.App

_Last updated: 2026-08-21_

`CashSloth.App` is the .NET 10 WPF point-of-sale client (`CSV2.exe`). It calls the native C++ core through P/Invoke and uses `CashSloth.Server` API v1 for central identity, presets, and reference data.

## Central-server integration

- Import and explicitly confirm a `.cashsloth-trust` document.
- Pin the server ID, signing-key ID, ECDSA P-256 public key, and fingerprint.
- Pair each installation with a short-lived server code and a per-installation proof-of-possession key.
- Register, sign in, refresh, sign out, and change passwords through `/api/v1/auth`.
- Enforce `User`, `Creator`, and `Admin` UI capabilities while the server remains authoritative.
- Let administrators approve/block users, change roles, and issue temporary password resets.
- List, download, create/update, and activate central presets according to server roles.
- Load central exchange rates and use the active-preset cache during a temporary outage while the locally verified access token remains valid.
- Discover, create, publish and join central events; receive realtime hints through SignalR with HTTP polling fallback.
- Persist a signed 12-hour event lease and an atomic SQLite sale outbox for offline continuation and later idempotent synchronisation.

Device keys and sessions are protected with Windows DPAPI under `%LocalAppData%\CashSloth\Client`. No local account database, default administrator, local-admin bypass, or anonymous preset-server client remains in the app. Old local account files from earlier versions are ignored and are not migrated to the central server.

## Accounts and presets UI

- Before the installation is trusted and paired, Accounts shows the one-time central-server setup. Once paired and signed out, it shows the central login plus collapsed self-registration only.
- Every signed-in user gets logout and password-change controls. Temporary-password sessions cannot use central preset functions until the password is changed.
- `Admin` alone sees account approval, role, enabled-state, and password-reset management.
- Presets lists all locally installed presets and supports create, activate, delete, and **Edit in shop**. Editing activates the selected preset and opens the existing catalog editor; catalog changes continue to persist into the active local preset.
- `User` and higher can browse and install all central presets. `Creator` and higher can upload the selected installed preset. Only `Admin` can also mark an uploaded preset active on the server.

## Local POS capabilities

- Catalog/category rendering and editing
- Cart add/remove/clear, totals, tendered amount, and change
- Customer display window
- Local assortment creation/editing/activation/deletion and central-preset cache/import
- Completed-sale SQLite history with event/register/operator metadata
- Payment method, tips, showcase filtering, and basic statistics
- Central multi-register event mode with frozen presets/rules, host/member controls, shared totals and customer-display register nick
- Named local history recordings, recoverable history reset, and CSV/PNG recording/event-report exports
- Localization, themes, startup animation, icon, and onboarding

Completed event sales and statistics are centralized in server V1.5. Mobile ordering and provider-backed payment results are not yet centralized.

## Build and test

```powershell
dotnet build src/CashSloth.App/CashSloth.App.csproj -c Release
dotnet test tests/CashSloth.App.Tests/CashSloth.App.Tests.csproj -p:SkipNativeCoreBuild=true
```
