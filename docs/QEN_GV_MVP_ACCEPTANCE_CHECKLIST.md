# QEN-GV MVP Acceptance Checklist

_Last updated: 2026-03-06_

Target milestone: **QEN-GV** (target date **2026-03-14**).

## Must-have flows
- [ ] Catalog initializes on startup and products render by category.
- [ ] Cart flow works: add item, remove selected line, clear cart.
- [ ] Payment flow works: preset tender buttons, custom CHF amount, reset given.
- [ ] Totals and change update correctly (`total_cents`, `given_cents`, `change_cents`).
- [ ] Customer display opens/closes and mirrors current cart snapshot.
- [ ] Edit mode works for existing items (select item, change name/price/category, save).
- [ ] Add flow works via popups: create category, add item into category, delete empty category.

## Minimum manual validation (Windows)
1. Build app: `dotnet build src/CashSloth.App/CashSloth.App.csproj`.
2. Start app (`CSV2.exe`) and verify product/category list is visible.
3. Add three different items, remove one line, then clear cart once.
4. Tender with preset buttons and one custom amount; verify "Missing" / "Return" / "Exact amount" hints.
5. Enable edit mode and update a product price; verify button price refreshes.
6. Open category manager, add a new category, add an item through `+`, and confirm persistence after restart.
7. Open customer display (single screen fallback accepted), then close it.

## Automated checks that must pass
- Core contract tests via CTest:
  - `cmake -S . -B build/core`
  - `cmake --build build/core --config Release`
  - `ctest --test-dir build/core -C Release --output-on-failure`
- App build:
  - `dotnet restore src/CashSloth.App/CashSloth.App.csproj`
  - `dotnet build src/CashSloth.App/CashSloth.App.csproj --no-restore -c Release`

## Verification snapshot (2026-03-06)
- [x] Local app build passed (`dotnet restore` + `dotnet build -c Release`).
- [x] Local core contract tests passed (`ctest` 4/4 via generated CMake build).
- [x] Latest CI run on `main` succeeded (`.github/workflows/ci.yml`, run `22778999078`).

## Exit criteria
- [ ] All must-have flow checks passed once on a rehearsal machine.
- [x] Automated checks passed on local run and CI.
- [ ] Blocking defects for rehearsal are resolved or explicitly waived.
