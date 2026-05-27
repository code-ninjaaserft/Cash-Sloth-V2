# Cash-Sloth v2

_Last updated: 2026-05-27_

## What is this?
Cash-Sloth v2 is a modular rebuild of the Cash-Sloth point-of-sale tooling, with a native C++ core and a WPF front end.

## Architecture
The design is layered: **Core (C++) -> C-API -> WPF (.NET) via P/Invoke**. The core owns business rules and data shaping; the C-API offers a stable ABI boundary; the WPF app focuses on presentation and workflow.

## Repository layout
```
.
|- .github/
|  `- workflows/
|- docs/
|- src/
|  |- CashSloth.App/
|  |- CashSloth.Core/
|  |- CashSloth.CoreApi/
|  `- CashSloth.PresetApi/
|- tests/
|  |- CashSloth.App.Tests/
|  `- CashSloth.Core.Tests/
|- tools/
|- CMakeLists.txt
|- Directory.Build.props
|- LICENSE
`- README.md
```

## Local builds (Windows)
```powershell
cmake -S . -B build/core
cmake --build build/core --config Release
ctest --test-dir build/core -C Release --output-on-failure
dotnet build src/CashSloth.App/CashSloth.App.csproj
```
The native build outputs `CashSlothCore.dll` under `build/core/bin/<Configuration>`, and the WPF project copies it to its output folder on build.
The release executable is `CSV2.exe` in `src/CashSloth.App/bin/Release/net8.0-windows/`.

### Visual Studio F5
1. Open `CashSloth.sln`.
2. Set `CashSloth.App` as the startup project (default).
3. Select Debug or Release.
4. Press F5.

Visual Studio will build the native core via CMake and copy `CashSlothCore.dll` into the app output folder. CMake must be installed and available on PATH.

## Design rules
- Monetary values in the core are stored as **int64 cents**.
- The C++ core is the single source of truth for business logic.
- The ABI boundary uses **JSON over `char*`** with an explicit **free** function pattern (see [docs/ABI.md](docs/ABI.md)).
- WPF calls into the C-API via P/Invoke; the C-API remains the only native boundary.

## Current status
The MVP stack is functional end-to-end:
- Core C-API supports catalog load/export, cart lifecycle, line add/remove/clear, totals, and payment given/change.
- WPF POS supports product/category selection, cart rendering, tender helpers, and customer display.
- Catalog edit mode supports add/edit/delete for products and categories (cart is reset after catalog changes).
- Accounts tab supports open self-registration as a normal `User`; only admins can promote roles or manage accounts.
- Preset web backend scaffold is available in `src/CashSloth.PresetApi` (SQLite + HTTP endpoints).
- WPF host can complete local sales into SQLite with event/register/user metadata, payment method, tip amount, recent history, and basic statistics.
- Showcase sales can be recorded without appearing in default history/statistics unless explicitly included.
- Native contract tests cover version, catalog, cart, and payment behavior via CTest.

## Roadmap
Planning and milestone detail live in [docs/ROADMAP.md](docs/ROADMAP.md) and [docs/MILESTONES.md](docs/MILESTONES.md). Dates are targets and may shift as scope is refined.
Current planning targets: **QEN-GV** (`2026-03-14`, closeout) and **Mobile Event Rollout** (`2026-07-05`).

## Local cleanup
- Remove build and IDE artifacts: `pwsh ./tools/clean_local_artifacts.ps1`
- Also remove local package caches: `pwsh ./tools/clean_local_artifacts.ps1 -IncludePackageCaches`
## Contribution workflow
- Use the issue templates for bugs, features, chores, and refactors.
- Run `tools/github/apply_github_setup.ps1` to sync labels, milestones, and seed issues via GitHub CLI (`gh`).

## Next steps
1. [ ] Run packaged-output smoke rehearsal and capture final QEN-GV sign-off notes.
2. [x] Maintain a concrete MVP acceptance checklist for QEN-GV (see `docs/QEN_GV_MVP_ACCEPTANCE_CHECKLIST.md`).
3. [x] Define baseline roadmap phases for the active milestone set (see `docs/ROADMAP.md`).
4. [ ] Add optional "remote-first" preset mode in `CashSloth.App` that consumes `CashSloth.PresetApi` directly for list/load/save/delete.

## Current milestone focus (through early Jul 2026)
- Target date: `2026-07-05`
- [ ] Add a **restaurant/festwirtschaft mode** with mobile ordering: customers can place orders from their phones, and those orders are sent to the host POS device for processing.
- [ ] Keep the Android app scope focused on **order send + payment to host POS**.
- [ ] Add **mobile payment support** for that flow, including phone-based **RFID/NFC** and **TWINT** handling, with payment results synced back to the host POS.
- [ ] Add **trinkgeld (tip) features** for card/mobile payments and cash flows.
- [ ] Add an **account system for all devices/users**: account creation is not limited to one operator.
- [ ] Keep **admin-only user controls** (for example role promotion and controlled user management/export).
- [x] Add **history + statistics** views for completed sales.
- [x] Add a **showcase mode** that is excluded from default history/statistics.
- [ ] Add an **event mode** where multiple users can open multiple tills/registers (for example, Kasse 1 and Kasse 2) under one event and sell in parallel.
- [ ] Add event analytics that can be filtered by **single register/user** and **overall event totals**.
- [ ] Add an in-app **tutorial/onboarding** flow.
- [ ] Add a **startup animation** for app launch.
- [ ] Apply **UI polish** items, including a proper window icon at the top left near minimize/maximize/close controls on Windows.

## License
MIT. See the `LICENSE` file.
