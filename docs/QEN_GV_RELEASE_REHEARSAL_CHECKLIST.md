# QEN-GV Release Rehearsal Checklist

_Last updated: 2026-03-06_

Scope: one dry run for the tag-driven Windows release flow before **2026-03-14**.

## Pre-flight
- [ ] Working tree is clean.
- [ ] CI workflow (`.github/workflows/ci.yml`) is green on `main`.
- [ ] Local release build succeeds:
  - `dotnet restore src/CashSloth.App/CashSloth.App.csproj`
  - `dotnet build src/CashSloth.App/CashSloth.App.csproj -c Release --no-restore`

## Release workflow rehearsal
1. Create and push a rehearsal tag (`v0.0.0-rc1` style).
2. Verify `.github/workflows/release.yml` completes on GitHub Actions.
3. Download produced ZIP artifact (`cash-sloth-v2-<tag>-windows.zip`).
4. Verify ZIP contents include:
   - `CSV2.exe`
   - `CashSlothCore.dll`
   - required runtime files from `dotnet publish` output.

## Smoke run from packaged output
- [ ] Launch `CSV2.exe` from extracted ZIP folder.
- [ ] Add item, tender amount, verify totals/change.
- [ ] Open/close customer display once.
- [ ] Enter edit mode and update one product.

## Sign-off
- [ ] Rehearsal run notes captured.
- [ ] Any blocking packaging defects filed and triaged.
- [ ] Team agrees release process is ready for QEN-GV.
