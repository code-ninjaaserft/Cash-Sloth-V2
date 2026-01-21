# Cash-Sloth v2

## What is this?
Cash-Sloth v2 is a fresh, modular rebuild of the Cash-Sloth point-of-sale tooling, with a native core and a modern WPF front end. This repository currently contains only the scaffolded layout and placeholders for future implementation.

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

## Design rules
- Monetary values in the core are stored as **int64 cents**.
- The C++ core is the single source of truth for business logic.
- The ABI boundary will use **JSON over `char*`** with an explicit **free** function pattern (planned, not implemented yet).
- WPF calls into the C-API via P/Invoke; the C-API remains the only native boundary.

## Status
Scaffold only — no functional code yet.

## Next steps
1. [ ] Create the WPF project (`CashSloth.App`) and solution structure.
2. [ ] Flesh out the C++ core library build (`CashSloth.Core`) via CMake.
3. [ ] Define minimal C-API entry points and data contracts (`CashSloth.CoreApi`).
4. [ ] Add a proof-of-life WPF P/Invoke call into the C-API.
5. [ ] Add CI workflows for build and test.

> Barcode scanning, database persistence, and preset management are later milestones and are **not** scaffolded here yet.

## License
MIT. See the `LICENSE` file.
