# QEN-GV Release Rehearsal Checklist

_Last updated: 2026-03-06_

Scope: one dry run for the tag-driven Windows release flow before **2026-03-14**.

## Pre-flight
- [x] Working tree is clean.
- [x] CI workflow (`.github/workflows/ci.yml`) is green on `main` (run `22778999078`, 2026-03-06).
- [x] Local release build succeeds:
  - `dotnet restore src/CashSloth.App/CashSloth.App.csproj`
  - `dotnet build src/CashSloth.App/CashSloth.App.csproj -c Release --no-restore`

## Release workflow rehearsal
- [x] Release tag push validated (`v2.0.0`).
- [x] `.github/workflows/release.yml` completed on GitHub Actions (run `22779133037`, 2026-03-06).
- [x] Produced ZIP artifact downloaded (`cash-sloth-v2-v2.0.0-windows.zip`).
- [x] ZIP contents verified:
  - `CSV2.exe`
  - `CashSlothCore.dll`
  - runtime files from `dotnet publish` output (`CSV2.dll`, `.deps.json`, `.runtimeconfig.json`)

## Smoke run from packaged output
- [ ] Launch `CSV2.exe` from extracted ZIP folder.
- [ ] Add item, tender amount, verify totals/change.
- [ ] Open/close customer display once.
- [ ] Enter edit mode and update one product.

## Sign-off
- [ ] Rehearsal run notes captured.
- [ ] Any blocking packaging defects filed and triaged.
- [ ] Team agrees release process is ready for QEN-GV.
