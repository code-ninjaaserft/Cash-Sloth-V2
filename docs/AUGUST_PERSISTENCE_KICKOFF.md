# August Persistence Kickoff

_Last updated: 2026-03-08_

This document captures the first implementation steps toward the August 2026 milestone (`Z'aemme aesse`) for persistence and migration safety.

## Scope of this kickoff

- Introduce a SQLite persistence path for assortment presets.
- Keep the existing JSON file path operational as compatibility fallback.
- Add explicit schema versioning groundwork for migrations.

## What is implemented now

### 1) SQLite store with schema versioning

New app-internal store:
- `src/CashSloth.App/AssortmentSqliteStore.cs`

Current schema (`user_version = 1`):
- `metadata (key, value)`
- `presets (id, name)`
- `preset_categories (preset_id, category)`
- `preset_items (preset_id, id, name, unit_cents, category)`

Behavior:
- If DB does not exist, schema is created.
- If DB schema version is higher than supported, load/save fails with explicit error.

### 2) Dual-path load/save (SQLite primary, JSON fallback)

`AssortmentPresetStore` now:
- Tries loading from SQLite first.
- Falls back to JSON if SQLite is absent/empty/unavailable.
- Mirrors successful JSON load back into SQLite.
- Continues writing JSON, and additionally mirrors writes into SQLite.

This keeps current behavior stable while starting migration work.

### 3) Build dependency for SQLite provider

`CashSloth.App.csproj` now includes:
- `Microsoft.Data.Sqlite` (8.0.x line) for .NET 8 compatibility.

### 4) Automated migration and fallback tests

`tests/CashSloth.App.Tests/AssortmentPresetStoreTests.cs` now covers:
- save and load through SQLite with JSON removed
- legacy JSON import into SQLite
- fallback to JSON when SQLite schema version is newer than supported
- clear error when only unsupported SQLite schema exists
- save failure path for unsupported SQLite schema versions

## Data ownership boundaries (current)

- **Core (C++)** owns runtime cart/payment state.
- **WPF app layer** owns assortment preset persistence storage strategy.
- **Assortment store contract** remains `CatalogItemEditor + extraCategories` for now.

## Migration strategy baseline (next steps)

1. Switch to SQLite as the single source of truth after soak validation.
2. Keep one-way JSON import only (remove dual-write after freeze window).
3. Add migration `v1 -> v2` script path in code (no manual DB edits).
4. Add contract/integration tests for:
   - cold start with JSON-only data
   - cold start with SQLite-only data
   - corrupted metadata / unsupported schema version

## Open risks

- Dual-write period can drift if one backend write fails silently.
- Future preset features (multiple active contexts) may require schema extensions.
