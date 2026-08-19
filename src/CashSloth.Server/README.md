# CashSloth.Server

`CashSloth.Server` is the Windows control center and central API for CSV2. The WPF application hosts ASP.NET Core/Kestrel in the same process on `127.0.0.1:5080` and starts a verified Cloudflare Tunnel child process when the operator clicks **Start**.

## V1 scope

- Central username/password accounts with approval and `User`, `Creator`, `Admin` roles
- Per-installation ECDSA P-256 device pairing and proof-of-possession
- ES256 access tokens (12 hours) and rotating hashed refresh tokens (30 days)
- Versioned presets with optimistic concurrency
- CHF-based Frankfurter v2 exchange-rate snapshots
- Local translation dictionary/cache
- Audit log, local snapshots, encrypted migration backup and restore
- MAMP-style dashboard, setup, emergency administration and tray operation

Sales, history, mobile orders, SignalR, TWINT and RFID remain outside V1.

## Cloudflare one-time setup

1. Add or use a domain in Cloudflare.
2. In Cloudflare Zero Trust, create a remotely managed Tunnel.
3. Add a published application such as `api.example.ch` and route it to `http://localhost:5080`.
4. Copy the tunnel token. Store it only in the server UI; CashSloth protects it with Windows DPAPI.
5. Fetch the pinned vendor binary:

   ```powershell
   .\tools\cloudflared\Get-Cloudflared.ps1
   ```

The script downloads Cloudflare's official Windows x64 build, checks its pinned SHA-256 value and requires a valid Cloudflare Authenticode signer. The app runs it as `cloudflared tunnel --no-autoupdate run`, passing the token only through `TUNNEL_TOKEN`. A Windows Job Object kills the tunnel if the UI process crashes.

## First start

1. Build or launch `CashSloth.Server.exe`.
2. Create the first administrator in the local setup panel. There is no default password and no `admin/admin` path.
3. Under **Einstellungen**, enter the public HTTPS URL, `cloudflared.exe` path, data path and tunnel token.
4. Click **Start**. The sequence is: validate → pre-migration backup/migrate → Wake-Guard → Kestrel → local healthcheck → tunnel → public healthcheck.
5. Export a `.cashsloth-trust` file and compare its fingerprint while importing it on every CSV2 installation.
6. Generate a ten-character pairing code, import trust in CSV2 and pair that installation within ten minutes.

The API is never bound to LAN or public interfaces. No Windows firewall rule is needed. If the server PC, internet connection or tunnel is off, central operations are unavailable.

## Data and secrets

Default location: `%LocalAppData%\CashSloth\Server`

- `cashsloth.server.sqlite3`: EF Core SQLite database (WAL, foreign keys, busy timeout)
- `server-signing-key.bin`: DPAPI-protected ECDSA signing key
- `tunnel-token.bin`: DPAPI-protected Cloudflare token
- `data-protection-keys`: ASP.NET Core Data Protection key ring
- `backups`: latest ten consistent SQLite snapshots

Private keys, passwords and tokens are never command-line arguments and are never written to application logs. A complete `.cashsloth-server-backup` decrypts DPAPI secrets inside an AES-256-GCM envelope derived from the operator passphrase with PBKDF2-SHA256 (600,000 iterations), so a controlled move to another PC preserves server trust.

## API

Public endpoints:

- `GET /health/live`
- `GET /api/v1/server/info`
- `POST /api/v1/devices/pair`
- `POST /api/v1/devices/challenge`
- `POST /api/v1/auth/register`, `/login`, `/refresh`

Authenticated endpoint groups:

- `/api/v1/auth`
- `/api/v1/presets`
- `/api/v1/reference`
- `/api/v1/admin/accounts`, `/devices`, `/audit`, `/translations`

Errors use `{ code, message, fieldErrors?, traceId }`. CORS is intentionally not enabled. Request bodies are limited to 1 MiB and pairing/auth endpoints have separate strict rate limits.

## Development

```powershell
dotnet build CashSloth.sln -p:SkipNativeCoreBuild=true
dotnet test tests\CashSloth.Server.Tests\CashSloth.Server.Tests.csproj
```

The initial EF migration is committed in `Data/Migrations`. All managed projects target .NET 10 LTS.

## MSIX release

Install the Windows SDK so `makeappx.exe` and `signtool.exe` are available, then run:

```powershell
.\packaging\Build-ServerMsix.ps1 `
  -Version 1.0.0.0 `
  -Publisher 'CN=CashSloth Internal' `
  -CertificatePath 'D:\secure\cashsloth-signing.pfx' `
  -CertificatePassword (Read-Host -AsSecureString)
```

The script downloads/verifies `cloudflared`, publishes a self-contained `win-x64` application, creates the full-trust MSIX and verifies its signature. The PFX and its private key must stay outside Git. Install the corresponding public certificate once on controlled PCs to avoid the unknown-publisher warning.
