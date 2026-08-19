# CSV2 Bucketlist

_Last updated: 2026-08-19_

This is the working status list for the Windows POS and local operational features. Server, multi-device, deployment, and third-party work is tracked in [CSV2 Server & External Work](CSV2_SERVER_EXTERNAL_BUCKETLIST.md).

## Done

- Native core and WPF POS run end to end on .NET 10/Windows.
- Shop/catalog, cart, amount handling, payment, change, and complete-sale flows work.
- Product and category add/edit/delete flows persist locally.
- Local preset persistence uses SQLite with JSON compatibility/migration support.
- Completed-sale history and basic statistics include event, register, operator, payment method, tip, and line metadata.
- Showcase sales are excluded from default history/statistics and can be included explicitly.
- Event UI supports LAN register discovery, saved register lists, and locally available total/selected-register statistics.
- Central server v1 trust import, device pairing, registration, login/refresh/logout, forced password change, role-based UI, and administrator account management are integrated.
- Central presets use the authenticated API v1 path; the active preset is cached locally for limited offline operation.
- The retired local SQLite account system, local-admin bypass/default credentials, and anonymous preset-provider path have been removed from the WPF app.
- Central exchange rates feed the existing local display-rate fallback.
- Startup animation, CashSloth logo/window icon, first-run onboarding, and reopenable tutorial are implemented.
- Visual Studio can build the native core and copy `CashSlothCore.dll` into the WPF output.

## Functional but still needs polish or field validation

- Touch targets, quick tender changes, and readability need another pass on the actual laptop/tablet hardware.
- Responsive layouts need testing at all supported resolutions and Windows scaling levels.
- Fixed UI text added after the first localization pass is not yet uniformly translated.
- Central-server onboarding should be rehearsed end to end on fresh Windows users: trust, pairing, pending approval, first login, temporary password, and preset load.
- Offline behavior needs a deliberate field test before event use; only the active preset cache and a locally valid access token are available offline.
- Z'Ämme ässe and full CSV2 currently depend on the selected branch/profile, so build/release instructions must state which variant they package.
- App-level fullscreen/topmost kiosk behavior is practical but is not hardened Windows Assigned Access or Shell Launcher.

## To do

- Finalize and field-test the local cash-tip workflow.
- Run the final Z'Ämme ässe packaged-output smoke test.
- Finish central-server/client localization and actionable connection-state messages.
- Continue touch, tab, and laptop-layout polish.
- Decide whether to deploy a hardened Windows kiosk configuration later.
- Add UI automation or a repeatable manual checklist for the WPF trust/pairing/account workflow.

## Explicit v1 boundary

Server v1 does not centralize sales, history, orders, payment state, or event totals. Those items remain in the external bucketlist rather than being implied by the completed account/preset integration.
