# car-tracker ("Garage")

Self-hosted web app to track mods, maintenance and costs for multiple cars — for you and a few friends, invite-only.

**Stack:** ASP.NET Core (.NET 11) + EF Core/SQLite · React + TypeScript + Vite (installable PWA) · Docker + Cloudflare Tunnel.

```
src/CarTracker.Api   backend (minimal APIs, Identity, EF Core, Magick.NET for photos)
src/web              frontend (React SPA; built into the API's wwwroot in Docker)
tests/               xUnit (unit + API integration tests)
deploy/              Dockerfile, docker-compose.yml (app + cloudflared)
```

## Local development

Requirements: .NET 11 SDK, Node 24+. Run the commands from the repository root.

```bash
dotnet run --project src/CarTracker.Api --launch-profile http   # API on :5080
```

```bash
npm --prefix src/web run dev   # UI on :5173 (proxies /api); first time: npm --prefix src/web install
```

On first start (empty DB) the API logs a one-time **admin invite link** (valid 24 h, new one on every start
until the admin exists). Open it, create the admin account, then invite friends from the 👥 page.

Dev data lives in `src/CarTracker.Api/_data/` (SQLite + photos), git-ignored.
To start over with an empty DB: stop the API, delete that folder, start again → new admin link in the log.

### Bill import (AI)

Your OpenAI key goes into .NET user-secrets (stored outside the repo, never committed):

```bash
dotnet user-secrets set "Ai:ApiKey" "<your key>" --project src/CarTracker.Api
```

Without a key, uploads end up as "Needs manual input" (you can still fill in the bill by hand).
To click through the import UI without spending credits, start the API with the fake extractor:

```bash
Ai__Provider=Fake dotnet run --project src/CarTracker.Api --launch-profile http
```

The same key and setup are used for the fuel photo scan (pump display or receipt). With `Ai__Provider=Fake`
the scan returns a fixed sample reading.

Settings (`appsettings.json` → `Ai`): `Model` (default `gpt-5-mini`), `ReasoningEffort`, prices for the
cost estimate, `MonthlyLimitPerUser` (bill imports) and `MonthlyFuelScansPerUser` (fuel scans, default 100);
admins are unlimited. Admins see usage per user on the 👥 page.

### Tests

```bash
dotnet test
```

DB schema changes: `dotnet ef migrations add <Name> -p src/CarTracker.Api -o Data/Migrations`
(migrations are applied automatically on startup).

## Deployment

Releases are Git tags (`v1.2.0`): GitHub Actions tests and publishes a Docker image, the server installs it
with `update.sh` (DB backup, health check, automatic rollback). Server setup (Docker + Cloudflare Tunnel),
releasing and operations: **[docs/DEPLOY.md](docs/DEPLOY.md)**.

### Notes
- No ports are published: only `cloudflared` can reach the app. That's why `TrustForwardedHeaders=true`
  is safe there. If you publish port 8080 to the LAN for debugging, be aware of that.
- Uploads are limited to 100 MB per request (Cloudflare free plan); the app uploads one photo per request.
- Bills are read in the background (Cloudflare cuts requests after 100 s); the UI polls until done.
  The bill file is sent to OpenAI with `store: false`.
- Photos are converted to JPEG (HEIC from iPhones works), EXIF rotation applied, **all metadata incl. GPS stripped**;
  the capture date is kept for the future timeline.

## License

[MIT](LICENSE)
