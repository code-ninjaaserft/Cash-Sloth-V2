# Issue E — Copy core DLL to WPF output

**Suggested labels:** `wpf`, `build`, `mvp`

## Description
Document the MVP guidance for ensuring the built core DLL is present in the WPF output directory during local builds.

## Definition of Done
- A short note explains the expected DLL location after a local build on Windows.
- The guidance references existing build behavior without adding new tooling.

## Task checklist
- [ ] Identify the current core DLL output path from the CMake build.
- [ ] Describe how the WPF project picks up or copies the DLL today.
- [ ] Note any manual steps needed for local dev until automated.
