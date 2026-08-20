# Public `zaa` event branch

The public `zaa` branch is a temporary stabilization branch for the upcoming
Zamme Aesse event. It exists so event work can be tested and reviewed normally
without creating a GitHub release for every change.

## Scope

- Keep the Stand 11 first-run catalog and the lean Zamme Aesse profile usable.
- Prioritize a clean cashier UI, reliability, and quick event-hardware testing.
- Keep the POS compatible with the shared CashSloth Server API v1.
- Allow kiosk mode to be enabled from Settings; it must remain off by default.

## Branch rules

- `main` remains the product source of truth and the destination for reusable
  CashSloth functionality.
- Synchronization is one-way: selected changes from `main` may be merged or
  cherry-picked into `zaa`, but the `zaa` branch itself must never be merged
  back into `main`.
- A generally useful idea discovered on `zaa` must be implemented separately on
  `main`, or transferred as an explicitly reviewed standalone commit.
- `zaa` should not grow a separate feature roadmap or alternative architecture.
- Ordinary testing happens directly from the branch or a local development ZIP.
- GitHub releases are reserved for explicit event test checkpoints and the final
  deployment candidate.

## Retirement

Once the regular application is ready for future Zamme Aesse events, use the
normal application and archive this emergency branch instead of merging it back
or maintaining two products indefinitely.
