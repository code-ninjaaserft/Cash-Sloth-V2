# Cash-Sloth v2 (CSV2)

_Last updated: 2026-08-21_

Cash-Sloth v2 is a Windows point-of-sale system with a native C++ transaction core, a WPF cashier application, and a separate WPF central-server control center.

## Architecture

```text
CashSloth.Core (C++)
        |
        | stable C ABI + JSON / P/Invoke
        v
CashSloth.App (WPF POS) <---- HTTPS / API v1 ----> CashSloth.Server (WPF + ASP.NET Core)
        |                                                |
        | local operational data                         | central Identity/SQLite data
        v                                                v
catalog cache, sale history, settings              accounts, devices, presets,
                                                   reference data, audit, backups
```

`CashSloth.Contracts` is the shared wire contract for server API v1. The central server is authoritative for accounts, roles, approvals, paired devices, central presets, exchange rates, and translations. Sales/history and event-register discovery remain local in v1.

The retired local SQLite account store and the old anonymous `CashSloth.PresetApi` path are no longer part of the WPF application.

## Repository layout

```text
.
|- .github/workflows/            CI and release workflows
|- docs/                         roadmap, status, and historical checklists
|- packaging/                    central-server MSIX packaging
|- src/
|  |- CashSloth.App/             WPF point-of-sale application (`CSV2.exe`)
|  |- CashSloth.Contracts/       shared API v1 records and roles
|  |- CashSloth.Core/            native C++ core and exported C ABI
|  |- CashSloth.CoreApi/         reserved standalone ABI-package boundary
|  `- CashSloth.Server/          WPF central server and ASP.NET Core host
|- tests/
|  |- CashSloth.App.Tests/
|  |- CashSloth.Core.Tests/
|  `- CashSloth.Server.Tests/
|- tools/cloudflared/            pinned tunnel acquisition and notices
|- CashSloth.sln
`- CMakeLists.txt
```

## Build and test on Windows

The managed projects target .NET 10. Building `CashSloth.App` normally also builds and copies `CashSlothCore.dll`.

```powershell
dotnet restore CashSloth.sln
dotnet build CashSloth.sln -p:SkipNativeCoreBuild=true
dotnet test CashSloth.sln -p:SkipNativeCoreBuild=true --no-build

cmake -S . -B build/core
cmake --build build/core --config Release
ctest --test-dir build/core -C Release --output-on-failure

dotnet build src/CashSloth.App/CashSloth.App.csproj -c Release
```

The POS executable is generated below `src/CashSloth.App/bin/Release/net10.0-windows/`. Open `CashSloth.sln` and select either `CashSloth.App` or `CashSloth.Server` as the Visual Studio startup project.

## Central server v1 workflow

1. Start `CashSloth.Server` and create the first administrator. There are no default credentials.
2. Configure the public HTTPS URL, data directory, verified `cloudflared.exe`, and tunnel token.
3. Start the server, export its `.cashsloth-trust` file, and compare the displayed fingerprint on the POS device.
4. Import the trust file in the CSV2 Accounts tab and pair the installation with a short-lived server-generated code.
5. Register or sign in. New self-registered users require administrator approval.

After pairing, the Accounts tab shows only sign-in/registration while signed out. Signed-in users see their own session and password controls; only administrators see account management. The Presets tab lists installed local presets for everyone, makes central presets downloadable for signed-in users, exposes publishing to `Creator` and `Admin`, and reserves server-wide activation for `Admin`.

The client stores the device private key and session with Windows DPAPI, validates ES256 access tokens against the pinned server key, rotates refresh tokens, and caches the active central preset for limited offline use while the signed access token remains valid. See [CashSloth.Server](src/CashSloth.Server/README.md) and [CashSloth.App](src/CashSloth.App/README.md) for details.

## Current status

- Native catalog/cart/payment contracts and CTest coverage are in place.
- The WPF POS supports catalog editing, tender/change, customer display, local presets, central multi-register events, offline event-sale queuing, recordings, recoverable history reset and CSV/PNG reports.
- Central server v1 provides paired-device authentication, central accounts and roles, versioned presets, reference data, audit, backups, and its Windows control UI.
- The WPF POS uses only central server v1 for account and remote-preset operations, with role-specific `User`/`Creator`/`Admin` surfaces.
- Central event-sale synchronization and cross-register totals are implemented in V1.5. Mobile ordering and provider-backed TWINT/NFC payments remain future work.

Current planning and status live in [docs/README.md](docs/README.md), especially the [CSV2 bucketlist](docs/CSV2_BUCKETLIST.md) and [server/external bucketlist](docs/CSV2_SERVER_EXTERNAL_BUCKETLIST.md).

## Design rules

- Monetary values in the core use signed 64-bit cents.
- The native core owns cart and payment business rules.
- The only native boundary is the documented C ABI with JSON over `char*` and explicit freeing; see [docs/ABI.md](docs/ABI.md).
- Central API errors use a shared structured contract, and authorization is enforced by the server rather than trusted to the UI.
- Secrets, generated databases, build output, and private signing material must not be committed.

## License

MIT. See [LICENSE](LICENSE).
