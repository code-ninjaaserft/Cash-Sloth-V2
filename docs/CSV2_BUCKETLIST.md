# CSV2 Bucketlist

_Last updated: 2026-08-21_

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
- Accounts hides signed-out/session/admin surfaces by state and role; the Presets workspace supports local create/edit/activate/delete, user downloads, creator uploads, and admin server activation.
- The retired local SQLite account system, local-admin bypass/default credentials, and anonymous preset-provider path have been removed from the WPF app.
- Central exchange rates feed the existing local display-rate fallback.
- Central event mode supports draft/publication/join, unique event nicks, frozen presets/rules, host member controls, realtime/poll updates and 12-hour offline sale queuing.
- Local history supports named recordings, CSV/PNG exports and confirmed recoverable reset/restore.
- Startup animation, CashSloth logo/window icon, first-run onboarding, and reopenable tutorial are implemented.
- Visual Studio can build the native core and copy `CashSlothCore.dll` into the WPF output.

## Functional but still needs polish or field validation

- Touch targets, quick tender changes, and readability need another pass on the actual laptop/tablet hardware.
- Responsive layouts need testing at all supported resolutions and Windows scaling levels.
- Fixed UI text added after the first localization pass is not yet uniformly translated.
- Central-server onboarding should be rehearsed end to end on fresh Windows users: trust, pairing, pending approval, first login, temporary password, and preset load.
- Offline event behavior needs a deliberate multi-laptop field test; the signed event lease and queued sales are implemented but not yet soak-tested on event hardware.
- Z'Ämme ässe and full CSV2 currently depend on the selected branch/profile, so build/release instructions must state which variant they package.
- App-level fullscreen/topmost kiosk behavior is practical but is not hardened Windows Assigned Access or Shell Launcher.

## To do

- Finalize and field-test the local cash-tip workflow.
- Run the final Z'Ämme ässe packaged-output smoke test.
- Finish central-server/client localization and actionable connection-state messages.
- Continue touch, tab, and laptop-layout polish.
- Decide whether to deploy a hardened Windows kiosk configuration later.
- Add UI automation or a repeatable manual checklist for the WPF trust/pairing/account workflow.

## Explicit V1.5 boundary

Server V1.5 centralizes completed event sales, event history and event totals. Ordinary non-event sales, mobile orders, and provider payment state remain local/future work.
