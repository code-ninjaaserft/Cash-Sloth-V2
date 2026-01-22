# Cash-Sloth v2

## What is this?
Cash-Sloth v2 is a fresh, modular rebuild of the Cash-Sloth point-of-sale tooling, with a native core and a modern WPF front end. This repository currently contains a buildable scaffold: a minimal C++ core DLL with a C-API boundary and a WPF shell that calls into it.

## Architecture
The design is layered: **Core (C++) → C-API → WPF (.NET) via P/Invoke**. The core owns business rules and data shaping; the C-API offers a stable ABI boundary; the WPF app focuses on presentation and workflow.

## Repository layout
```
.
├── .github/
│   └── workflows/
├── docs/
├── src/
│   ├── CashSloth.App/
│   ├── CashSloth.Core/
│   └── CashSloth.CoreApi/
├── tests/
│   ├── CashSloth.App.Tests/
│   └── CashSloth.Core.Tests/
├── tools/
├── CMakeLists.txt
├── Directory.Build.props
├── LICENSE
└── README.md
```

## Local builds (Windows)
```powershell
cmake -S . -B build/core
cmake --build build/core --config Release
ctest --test-dir build/core -C Release --output-on-failure
dotnet build src/CashSloth.App/CashSloth.App.csproj
```
The native build outputs `CashSlothCore.dll` to `build/core/bin`, and the WPF project copies it to its output folder on build.

## Design rules
- Monetary values in the core are stored as **int64 cents**.
- The C++ core is the single source of truth for business logic.
- The ABI boundary uses **JSON over `char*`** with an explicit **free** function pattern (see [docs/ABI.md](docs/ABI.md)).
- WPF calls into the C-API via P/Invoke; the C-API remains the only native boundary.

## Status
Core DLL + WPF shell are wired up, with a minimal cart MVP (add/remove/clear/total) in the native core.

## Roadmap
Planning and milestone detail live in [docs/ROADMAP.md](docs/ROADMAP.md) and [docs/MILESTONES.md](docs/MILESTONES.md). Dates are targets and may shift as scope is refined.

## Contribution workflow
- Use the issue templates for bugs, features, chores, and refactors.
- Run `tools/github/apply_github_setup.ps1` to sync labels, milestones, and seed issues via GitHub CLI (`gh`).

## Next steps
1. [ ] Add solution files and test projects.
2. [ ] Expand C-API data contracts for catalog/pricing.
3. [ ] Add CI workflows for build and test.

> Barcode scanning, database persistence, and preset management are later milestones and are **not** scaffolded here yet.

## License
MIT. See the `LICENSE` file.
