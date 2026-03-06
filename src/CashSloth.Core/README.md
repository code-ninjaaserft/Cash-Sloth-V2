# CashSloth.Core

Native C++ core DLL built via CMake.

Current C-API scope:
- lifecycle and error handling (`cs_init`, `cs_shutdown`, `cs_last_error`, `cs_free`)
- catalog import/export as JSON
- cart handles with add/remove/clear and total calculation
- payment tendered amount and change queries

Public header: `src/CashSloth.Core/include/cashsloth_core.h`.
ABI rules: `docs/ABI.md`.
