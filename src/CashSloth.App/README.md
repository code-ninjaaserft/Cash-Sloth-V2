# CashSloth.App

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

Device keys and sessions are protected with Windows DPAPI under `%LocalAppData%\CashSloth\Client`. No local account database, default administrator, local-admin bypass, or anonymous preset-server client remains in the app. Old local account files from earlier versions are ignored and are not migrated to the central server.

## Local POS capabilities

- Catalog/category rendering and editing
- Cart add/remove/clear, totals, tendered amount, and change
- Customer display window
- Local assortment persistence and central-preset cache/import
- Completed-sale SQLite history with event/register/operator metadata
- Payment method, tips, showcase filtering, and basic statistics
- LAN register advertisement/discovery and locally aggregated event views
- Localization, themes, startup animation, icon, and onboarding

Sales, payment results, order synchronization, and cross-device event databases are not centralized in server v1.

## Build and test

```powershell
dotnet build src/CashSloth.App/CashSloth.App.csproj -c Release
dotnet test tests/CashSloth.App.Tests/CashSloth.App.Tests.csproj -p:SkipNativeCoreBuild=true
```

The Zamme Aesse profile uses the same server v1 client while retaining its Stand 11 first-run catalog and lean feature configuration.
