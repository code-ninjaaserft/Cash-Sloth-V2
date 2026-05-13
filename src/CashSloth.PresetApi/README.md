# CashSloth.PresetApi

Lightweight HTTP API for sharing CashSloth presets across devices.

## What It Stores

- Presets in SQLite (`presets`, `preset_items`, `preset_categories`).
- Active preset id in `metadata.active_preset_id`.
- JSON schema compatible with `CashSloth.App` import/export format.

## Configure DB Path

- Environment variable: `PRESET_DB_PATH`
- Fallback path: `./data/cashsloth.presets.sqlite3` (relative to API binary)

## Run Locally

```powershell
dotnet run --project src\CashSloth.PresetApi\CashSloth.PresetApi.csproj
```

Default URL is usually `http://localhost:5000` (or an ASP.NET-assigned port).

## API Endpoints

- `GET /health`
- `GET /api/presets` -> full store document
- `GET /api/presets/{presetId}` -> one preset
- `POST /api/presets/upload?setActive=true|false` -> upload one preset JSON
- `PUT /api/presets/{presetId}?setActive=true|false` -> upsert preset under URL id
- `PUT /api/presets/{presetId}/active` -> set active preset
- `DELETE /api/presets/{presetId}` -> delete preset

## Connect From CashSloth.App

- **Import online preset**:
  - put a URL like `https://your-host/api/presets/MY_PRESET` into **Preset URL**
  - click **Import online preset**
- **Upload active preset**:
  - put a URL like `https://your-host/api/presets/upload` into **Preset URL**
  - select local preset
  - click **Upload active preset**

## Move It Online

1. Deploy `CashSloth.PresetApi` to a web host (VPS, Azure App Service, Render, Fly.io, etc.).
2. Attach persistent storage and point `PRESET_DB_PATH` to that mounted folder.
3. Put a reverse proxy / TLS certificate in front (Nginx, Caddy, Traefik, or platform-managed HTTPS).
4. Use HTTPS URLs from the app.
