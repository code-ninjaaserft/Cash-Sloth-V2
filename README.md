# Cash-Sloth v2

_Last updated: 2026-03-06_

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
|  `- CashSloth.CoreApi/
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
- Native contract tests cover version, catalog, cart, and payment behavior via CTest.

## Roadmap
Planning and milestone detail live in [docs/ROADMAP.md](docs/ROADMAP.md) and [docs/MILESTONES.md](docs/MILESTONES.md). Dates are targets and may shift as scope is refined.
Current planning target: **QEN-GV in mid-March 2026** (`2026-03-14`).

## Contribution workflow
- Use the issue templates for bugs, features, chores, and refactors.
- Run `tools/github/apply_github_setup.ps1` to sync labels, milestones, and seed issues via GitHub CLI (`gh`).

## Next steps
1. [ ] Harden release packaging and rehearsal checklist for QEN-GV.
2. [ ] Prepare persistence and preset handoff for the Z'Ämme ässe phase.

> Barcode scanning, database persistence, and preset management are later milestones and are **not** scaffolded here yet.

## License
MIT. See the `LICENSE` file.
