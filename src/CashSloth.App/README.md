# CashSloth.App

WPF (.NET 8) POS shell that calls the native core via P/Invoke.

Current MVP capabilities:
- load the default catalog into core on startup
- render product buttons with category filtering
- add/remove/clear cart lines and show totals
- track tendered amount (preset + custom CHF) and change
- open/close customer display window (second screen)
- edit mode for adding/updating/deleting products and categories
- separate edit popups for adding items and managing categories (+ / X actions)
- open self-registration for normal user accounts, with admin-only role promotion
- persist assortment presets locally (SQLite + JSON snapshot)
- import presets from HTTP/HTTPS URLs and upload active presets to a web endpoint
- complete sales into local SQLite history with event/register/user metadata
- track payment method, tips, showcase mode, recent sales, and basic statistics
