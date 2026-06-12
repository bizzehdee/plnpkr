# plnpkr

**plnpkr** (planning poker) — real-time Scrum estimation: multiple users, multiple independent sessions, communicating over
SignalR (WebSockets). .NET 10 backend + Angular 21 (Bootstrap 5) frontend; persistence via EF Core with a
configurable provider (SQLite by default; SQL Server / PostgreSQL also supported).

## Features

- Create named sessions; share a short invite link (`/join/<code>`).
- Card decks: Sequential, Fibonacci, Modified Fibonacci, T-shirt, Powers of two, or Custom.
- Roles: **voters** estimate, **observers** watch; an optional **organiser** (the creator) reveals/resets.
- Reveal manually or **auto-reveal** once every (connected) voter has voted.
- Votes stay editable after reveal (with an "edited" marker); average/consensus recompute live.
- No accounts — pick a display name (remembered per browser); names are unique per session.
- Optional **session password** (organiser can set / change / clear; stored as a PBKDF2 hash, never plaintext).
- Resilient: a dropped connection keeps your seat/vote and reconnects; idle sessions are evicted.
- Light / dark / system theme, remembered locally.
- Optional **issue-tracker integration** (Jira / Azure DevOps, behind a flag): connect with your own
  account, link a ticket to see its title + rich description (with acceptance criteria), submit the
  agreed story points back, and work through a queue loaded from a board/query URL or ID list.
- Real **health checks** (`/health` readiness incl. DB, `/health/live` liveness) and a containerised
  run (`docker compose up`).

## Layout

```
backend/    .NET 10 solution (Core = logic, Data = EF Core model/DbContext + a project per engine
            (Data.Sqlite / Data.SqlServer / Data.PostgreSql, each with its driver + migrations),
            Integrations = Jira/ADO adapters, Api = SignalR hub + MVC REST host) + tests
frontend/   Angular 21 app (Bootstrap 5, @microsoft/signalr)
```

## Run locally — one command

From the repo root, use the launcher (it checks prerequisites, installs/restores dependencies,
and starts everything). Press **Ctrl+C** to stop.

```bash
# macOS / Linux / Git Bash
./run.sh           # dev: API (:5210) + Angular dev server (:4200), open http://localhost:4200
./run.sh prod      # publish the single artifact and serve it on http://localhost:5210
./run.sh test      # backend tests + Core coverage gate + frontend tests
```

```powershell
# Windows PowerShell
./run.ps1          # dev   (also: ./run.ps1 prod | ./run.ps1 test)
```

### Or run the pieces by hand

```bash
# API → http://localhost:5210
cd backend && dotnet run --project src/PlanningPoker.Api --launch-profile http

# Angular dev server → http://localhost:4200 (talks to the API on :5210)
cd frontend && npm install && npm start
```

To try multiple users locally, open the invite link in a second browser/tab and join with a
different name.

### Or run it in Docker

One container serves the API, the SignalR hub, and the Angular SPA (the same single-artifact shape as
the production build below). SQLite is stored on a named volume so it survives restarts.

```bash
docker compose up --build      # → http://localhost:8080
```

The multi-stage [`Dockerfile`](./Dockerfile) builds the SPA and publishes the API; the image runs as a
non-root user. Configuration is passed as environment variables (e.g. `Integrations__Jira__Enabled=true` in
[`docker-compose.yml`](./docker-compose.yml); OAuth client id/secret via env, never baked in).

> Single instance only — in-process SignalR + local SQLite means **don't run multiple replicas** of
> this image as-is (scale up, not out).

## Testing locally

Prerequisites: **.NET 10 SDK**, **Node 20+**, and (for the coverage gate) **PowerShell 7** (`pwsh`).

### Everything in one shot

```bash
./run.sh test       # backend tests + Core coverage gate + frontend tests
./run.ps1 test      # same, on Windows PowerShell
```

This runs the same checks (backend + frontend tests + the Core coverage gate) you'd put in CI, so a green `test` locally means a clean build.

### Backend (xUnit) — 303 tests across Core / Integrations / Data / Api

```bash
cd backend
dotnet test PlanningPoker.slnx                       # whole solution

# One project at a time
dotnet test tests/PlanningPoker.Core.Tests           # fast, no I/O (the bulk of the logic) — 227
dotnet test tests/PlanningPoker.Integrations.Tests   # Jira/ADO adapters against stubbed HTTP — 28
dotnet test tests/PlanningPoker.Data.Tests           # EfSessionStore against real SQLite — 25
dotnet test tests/PlanningPoker.Api.Tests            # REST + SignalR + health over an in-memory server — 23

# Run a single test or class by name
dotnet test tests/PlanningPoker.Core.Tests --filter "FullyQualifiedName~VotingTests"

# Re-run on file changes while developing
dotnet watch test --project tests/PlanningPoker.Core.Tests
```

### Coverage gate — Core must be ≥ 90% line + branch (currently ~96% / ~91%)

```bash
pwsh backend/coverage-gate.ps1            # prints the numbers and fails if under threshold
pwsh backend/coverage-gate.ps1 -Threshold 0.95   # try a stricter bar
```

### Frontend (Vitest + Angular TestBed) — 63 specs across 5 files

```bash
cd frontend
npm ci                       # first time only
npm test -- --watch=false    # run once (CI mode)
npm test                     # watch mode while developing
```

### Manual / exploratory testing

```bash
./run.sh            # dev: API :5210 + Angular :4200 (Ctrl+C stops both)
```

- Open **http://localhost:4200**, create a session, then open the **invite link in a second
  browser or a private/incognito window** and join with a different name to act as another user —
  votes, reveal, auto-reveal, observers, away/reconnect, and the theme toggle all work live.
- State persists in a local SQLite file (`planningpoker.db` next to the API). To start clean,
  stop the app and delete it:
  ```bash
  rm -f backend/src/PlanningPoker.Api/planningpoker.db*
  ```
- To exercise the exact production build locally (SPA served from `wwwroot`, one origin):
  ```bash
  ./run.sh prod       # publishes and serves on http://localhost:5210
  ```

## Build a single deployable artifact

`dotnet publish` builds the Angular app and emits it into the API's `wwwroot`, so one process
serves both the API and the SPA (same origin, no CORS), with a SPA deep-link fallback.

```bash
dotnet publish backend/src/PlanningPoker.Api -c Release -o ./publish
# ./publish is self-contained: run `dotnet PlanningPoker.Api.dll` and browse the root.
# (Pass /p:BuildSpa=false to skip the Angular build for backend-only output.)
```

## Build the two apps separately (split deployment)

For hosting the API on a server and the SPA on static hosting (Azure Blob `$web`, AWS S3 + CloudFront,
Netlify, …), build each independently:

```bash
# 1. API only — no SPA bundled into wwwroot:
dotnet publish backend/src/PlanningPoker.Api -c Release -o ./out/api -p:BuildSpa=false
#    Deploy ./out/api to the server (run `dotnet PlanningPoker.Api.dll`).

# 2. SPA only — static files for the bucket/CDN:
cd frontend && npm ci && npm run build -- --configuration production
#    Upload everything under dist/frontend/browser/ to the static host.
```

Because they're now on different origins, two things must be wired up:

- **Point the SPA at the API.** Edit `config.js` *in the deployed static files* (no rebuild needed) and
  set the API origin:
  ```js
  window.__PP_CONFIG__ = { apiBase: "https://planning-poker-api.example.com" };
  ```
  REST and the SignalR hub both derive their URL from this. Leave it `""` for the same-origin
  single-artifact build above.
- **Allow the SPA origin on the API (CORS).** SignalR needs explicit origins + credentials, so set:
  ```jsonc
  // appsettings.json or env: Cors__AllowedOrigins__0=https://planning-poker.example.com
  "Cors": { "AllowedOrigins": [ "https://planning-poker.example.com" ] }
  ```
  (`http(s)://localhost:4200` is always allowed in Development for `ng serve`.)
- **SPA deep-link fallback on the static host.** Configure the bucket/CDN to serve `index.html` for
  unmatched paths (e.g. S3/CloudFront custom error response 404→`/index.html` 200; Azure Static Website
  error document = `index.html`) so routes like `/join/blue-fox-42` load the app.

## Deploy — single Azure App Service

Deploy the published output to one App Service instance. The chosen tier/size is a DevOps
decision; it must provide:

- **WebSockets: On** (Configuration → General settings) — otherwise SignalR degrades to long-polling.
- **Always On: On** — keeps in-memory SignalR state and the SQLite handle alive.
- **Single instance — do not scale out.** SignalR uses the in-process backplane and SQLite is a
  local file; scale **up**, not out. (Horizontal scale later = Azure SignalR Service + a server DB.)

### Provision an App Service

Create the site however you prefer (portal, `az` CLI, or your own IaC) — Linux, `DOTNETCORE|10.0` —
with the capabilities above and the SQLite connection string pointed at persistent storage:

- App setting `ConnectionStrings__Default = Data Source=/home/data/planningpoker.db`
- App setting `ASPNETCORE_ENVIRONMENT = Production`
- **B1** is the floor SKU (cheapest that supports WebSockets + Always On).

### Deploy the application

```bash
dotnet publish backend/src/PlanningPoker.Api -c Release -o ./publish
cd publish && zip -r ../app.zip . && cd ..
az webapp deploy -g <resource-group> -n <appName> --src-path app.zip --type zip
```

The app applies EF Core migrations and creates its SQLite directory automatically on startup, then
serves the API, SignalR hub, and Angular SPA from the one site.

> **Out of scope this iteration:** provisioning IaC and a CI/CD pipeline. `coverage-gate.ps1` runs the
> Core ≥90% gate locally; wire it (and the tests) into whatever pipeline you add later.

## License & attribution

**plnpkr is owned and maintained by Darren Horrocks**, and is open source under the
[Apache License 2.0](./LICENSE).

You are free to **use, run, modify, and contribute** to it — including commercially. In return, the
license requires that you **keep the attribution**:

- Retain the `LICENSE` and [`NOTICE`](./NOTICE) files (and the copyright notices) in any copy or fork.
- **Credit the project** — if you use, deploy, or build on it, you must visibly state that it's based
  on *plnpkr by Darren Horrocks* (see `NOTICE` for the wording and where it must appear).
- **Mark your changes** — modified files must carry a prominent notice saying you changed them.

Taking the source and shipping it — commercially or otherwise — **without that attribution is a breach
of the license**. Contributions are submitted under the same Apache 2.0 terms (inbound = outbound).
