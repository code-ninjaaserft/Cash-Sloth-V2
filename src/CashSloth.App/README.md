# CashSloth.App

WPF (.NET 8) POS shell that calls the native core via P/Invoke.

Current MVP capabilities:
- load the default catalog into core on startup
- render product buttons with category filtering
- add/remove/clear cart lines and show totals
- track tendered amount (preset + custom CHF) and change
- open/close customer display window (second screen)
- edit mode for adding/updating/deleting products and categories
