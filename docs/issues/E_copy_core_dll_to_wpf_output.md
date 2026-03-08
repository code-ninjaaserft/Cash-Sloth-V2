# Issue E — Copy core DLL to WPF output

**Suggested labels:** `wpf`, `build`, `mvp`
**Status (2026-03-06):** Completed

## Description
Document the MVP guidance for ensuring the built core DLL is present in the WPF output directory during local builds.

## Definition of Done
- A short note explains the expected DLL location after a local build on Windows.
- The guidance references existing build behavior without adding new tooling.

## Task checklist
- [x] Identify the current core DLL output path from the CMake build.
- [x] Describe how the WPF project picks up or copies the DLL today.
- [x] Note any manual steps needed for local dev until automated.

## Implementation note
- Core output path: `build/core-ninja/bin/CashSlothCore.dll` (local Ninja build) and `build/core/bin/Release/CashSlothCore.dll` (CI/release workflow).
- App copy behavior: `CashSloth.App.csproj` triggers native build/copy during app build.
- Manual fallback: build app once (`dotnet build`) to ensure DLL copy into `src/CashSloth.App/bin/<Configuration>/net8.0-windows/`.
