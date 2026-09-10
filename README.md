# TeamTools

**TeamTools** is a small platform for the ceremonies a team actually runs together, in real time and
without accounts. It hosts two tools over one shared room engine:

- **Planning Poker** — real-time Scrum estimation.
- **Team Retro** — real-time retrospectives, from writing cards to agreeing actions.

Both work the same way: someone creates a room, shares a short invite link, and everyone else joins
by picking a display name. No sign-up, no team setup, nothing to administer. .NET 10 backend +
Angular 21 (Bootstrap 5) frontend, over SignalR (WebSockets); persistence via EF Core with a
configurable provider (SQLite by default; SQL Server / PostgreSQL also supported).

[Support on Ko-Fi](https://ko-fi.com/bizzehdee)

## Planning Poker

- Create named sessions; share a short invite link (`/join/<code>`).
- Card decks: Sequential, Fibonacci, Modified Fibonacci, T-shirt, Powers of two, or Custom.
- Reveal manually or **auto-reveal** once every (connected) voter has voted.
- Votes stay editable after reveal (with an "edited" marker); average/consensus recompute live.
- Optional **round timer** (start / pause / resume / stop) with a server-authoritative deadline.
- Round history, per-session analytics, and a CSV/JSON export of completed rounds.
- Optional **issue-tracker integration** (Jira / Azure DevOps, behind a flag): connect with your own
  account, link a ticket to see its title + rich description (with acceptance criteria), submit the
  agreed story points back, and work through a queue loaded from a board/query URL or ID list.

## Team Retro

- Column templates: Went well / To improve / Action items, Start-Stop-Continue, 4Ls, Mad-Sad-Glad,
  or Custom columns supplied at creation.
- **Phases** the facilitator moves the room through — Collect → Group → Vote → Discuss → Actions —
  each with an optional countdown, and each gating what can be changed.
- **Hidden collection:** during Collect you see your own cards and a count of how many others are
  being written, so nobody anchors on what has already been said.
- Optional **anonymity**, chosen at creation and locked once the first card exists: the snapshot
  carries no authorship at all, for anyone. (Anonymity from participants, not from whoever
  administers the server — the UI says so.)
- **Grouping** cards into themes, by drag-and-drop *or* a keyboard equivalent for every drag.
- **Dot voting** with a per-participant budget enforced server-side; totals stay hidden while voting
  is open, then become a ranked discussion agenda.
- **Action items** with an owner, a due date and a done state — editable after the retro is closed,
  because "mark done" happens days later.
- **Carry-over:** a new retro can pull the unfinished actions from a previous board's code, so the
  review at the top of Collect is the first thing the team does.
- **Export** the finished board as Markdown, CSV or JSON, plus a read-only summary page at
  `/retro/<code>/summary` that a reader can open without joining the room.

## Shared by both tools

- Roles: **voters** take part, **observers** watch; an optional **organiser** (the creator) drives.
- Multiple organisers with automatic succession, so a facilitator dropping out doesn't strand a room.
- No accounts — pick a display name (remembered per browser); names are unique per room.
- Optional **room password** (organiser can set / change / clear; stored as a PBKDF2 hash, never
  plaintext).
- Resilient: a dropped connection keeps your seat, vote and dots, and reconnects; idle rooms are
  evicted on a published retention policy.
- Emoji reactions (ephemeral, never persisted).
- Light / dark / system theme, remembered locally.
- Four languages (en / es / pt / pl) with locale-aware numbers, plurals and dates.
- Accessibility baseline: keyboard equivalents for every pointer interaction, live-region
  announcements, focus management.
- Real **health checks** (`/health` readiness incl. DB, `/health/live` liveness) and a containerised
  run (`docker compose up`).

## Layout

```
backend/    .NET 10 solution:
              TeamTools.Core        room engine (tool-agnostic) + Poker/ and Retro/ namespaces
              TeamTools.Data        EF Core model + DbContext, provider-agnostic
              TeamTools.Data.{Sqlite,SqlServer,PostgreSql}
                                    one project per engine: driver + migrations
              TeamTools.Integrations  Jira / Azure DevOps adapters
              TeamTools.Api         host: PokerHub + RetroHub + REST controllers + health checks
            plus a test project per layer
frontend/   Angular 21 app (Bootstrap 5, @microsoft/signalr)
deploy/     Terraform + shell scripts for an AWS deployment (EC2 + S3 + CloudFront)
```

See [ARCHITECTURE.md](./ARCHITECTURE.md) for the design — the room / tool-payload split, the
real-time contract, and why the tools share an engine but nothing else.

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
cd backend && dotnet run --project src/TeamTools.Api --launch-profile http

# Angular dev server → http://localhost:4200 (talks to the API on :5210)
cd frontend && npm install && npm start
```

To try multiple users locally, open the invite link in a second browser/tab and join with a
different name.

### Or run it in Docker

One container serves the API, both SignalR hubs, and the Angular SPA (the same single-artifact shape
as the production build below). SQLite is stored on a named volume so it survives restarts.

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

This runs the same checks as CI, so a green `test` locally means a clean build.

### Backend (xUnit) — 620 tests across Core / Integrations / Data / Api

```bash
cd backend
dotnet test TeamTools.slnx                        # whole solution

# One project at a time
dotnet test tests/TeamTools.Core.Tests            # fast, no I/O (the bulk of the logic) — 504
dotnet test tests/TeamTools.Integrations.Tests    # Jira/ADO adapters against stubbed HTTP — 40
dotnet test tests/TeamTools.Data.Tests            # EfRoomStore + migrations against real SQLite — 35
dotnet test tests/TeamTools.Api.Tests             # REST + SignalR + health over an in-memory server — 41

# Run a single test or class by name
dotnet test tests/TeamTools.Core.Tests --filter "FullyQualifiedName~RetroVotingTests"

# Re-run on file changes while developing
dotnet watch test --project tests/TeamTools.Core.Tests
```

### Coverage gate — Core must be ≥ 90% line + branch (currently ~96% / ~92%)

```bash
pwsh backend/coverage-gate.ps1                   # prints the numbers and fails if under threshold
pwsh backend/coverage-gate.ps1 -Threshold 0.95   # try a stricter bar
```

### Frontend (Vitest + Angular TestBed) — 214 specs across 13 files

```bash
cd frontend
npm ci                       # first time only
npm test -- --no-watch       # run once (CI mode)
npm test                     # watch mode while developing
```

### Continuous integration

[`.github/workflows/ci.yml`](./.github/workflows/ci.yml) runs on every push and pull request to
`main`: the backend build + tests, the Core coverage gate, and the frontend production build +
tests. Superseded runs on the same ref are cancelled.

### Manual / exploratory testing

```bash
./run.sh            # dev: API :5210 + Angular :4200 (Ctrl+C stops both)
```

- Open **http://localhost:4200**, create a poker session or a retro, then open the **invite link in a
  second browser or a private/incognito window** and join with a different name to act as another
  user. Voting, reveal, auto-reveal, observers, away/reconnect, retro phases, grouping, dot voting
  and the theme toggle all work live.
- A retro is best exercised with two windows: write cards in both during Collect (each only sees its
  own, plus a count), then walk the phases from the facilitator's window.
- State persists in a local SQLite file (`teamtools.db` next to the API). To start clean, stop the
  app and delete it:
  ```bash
  rm -f backend/src/TeamTools.Api/teamtools.db*
  ```
  A database from before the rename (`planningpoker.db`) is still picked up if it is there, with a
  log hint to rename it — the app will not silently start against an empty database.
- To exercise the exact production build locally (SPA served from `wwwroot`, one origin):
  ```bash
  ./run.sh prod       # publishes and serves on http://localhost:5210
  ```

## Build a single deployable artifact

`dotnet publish` builds the Angular app and emits it into the API's `wwwroot`, so one process
serves both the API and the SPA (same origin, no CORS), with a SPA deep-link fallback.

```bash
dotnet publish backend/src/TeamTools.Api -c Release -o ./publish
# ./publish is self-contained: run `dotnet TeamTools.Api.dll` and browse the root.
# (Pass /p:BuildSpa=false to skip the Angular build for backend-only output.)
```

## Build the two apps separately (split deployment)

For hosting the API on a server and the SPA on static hosting (Azure Blob `$web`, AWS S3 + CloudFront,
Netlify, …), build each independently:

```bash
# 1. API only — no SPA bundled into wwwroot:
dotnet publish backend/src/TeamTools.Api -c Release -o ./out/api -p:BuildSpa=false
#    Deploy ./out/api to the server (run `dotnet TeamTools.Api.dll`).

# 2. SPA only — static files for the bucket/CDN:
cd frontend && npm ci && npm run build -- --configuration production
#    Upload everything under dist/frontend/browser/ to the static host.
```

Because they're now on different origins, three things must be wired up:

- **Point the SPA at the API.** Edit `config.js` *in the deployed static files* (no rebuild needed) and
  set the API origin:
  ```js
  window.__PP_CONFIG__ = { apiBase: "https://teamtools-api.example.com" };
  ```
  REST and both SignalR hubs derive their URL from this. Leave it `""` for the same-origin
  single-artifact build above. (The global keeps its `__PP_CONFIG__` name deliberately: it is set by
  a `config.js` that lives in *deployed* files, and renaming it would silently break any deployment
  whose config.js was not updated in the same breath as the bundle.)
- **Allow the SPA origin on the API (CORS).** SignalR needs explicit origins + credentials, so set:
  ```jsonc
  // appsettings.json or env: Cors__AllowedOrigins__0=https://teamtools.example.com
  "Cors": { "AllowedOrigins": [ "https://teamtools.example.com" ] }
  ```
  (`http(s)://localhost:4200` is always allowed in Development for `ng serve`.)
- **SPA deep-link fallback on the static host.** Configure the bucket/CDN to serve `index.html` for
  unmatched paths (e.g. S3/CloudFront custom error response 404→`/index.html` 200; Azure Static Website
  error document = `index.html`) so routes like `/join/blue-fox-42` load the app.

## Deploy

Whatever the target, three constraints hold:

- **WebSockets must be available** — otherwise SignalR degrades to long-polling.
- **The process must stay alive** (no idle shutdown), because SignalR groups and the SQLite handle
  are in-process.
- **Single instance — do not scale out.** Scale *up*. (Horizontal scale later = Azure SignalR
  Service / a Redis backplane + a server database.)

### AWS — Terraform + scripts

[`deploy/`](./deploy) provisions and updates a small AWS deployment: the API on EC2 behind a systemd
unit, the SPA in S3 served by CloudFront, optional Route53 + ACM for a custom domain.

```bash
cd deploy/terraform
cp terraform.tfvars.example terraform.tfvars   # fill in ec2_key_pair_name, domain, etc.
terraform init && terraform apply

# then, from the repo root, to ship a new build:
EC2_KEY=~/.ssh/my-key.pem bash deploy/scripts/deploy-backend.sh
bash deploy/scripts/deploy-frontend.sh
```

`terraform output` prints the URLs and the log command. The instance runs migrations on startup and
keeps SQLite under `/opt/teamtools/data`.

> **Upgrading an existing `planning-poker` stack:** `app_name` (the prefix on every AWS resource
> name) now defaults to `teamtools`. Applying that against a stack created under the old default
> would *replace* the bucket and the instance. Pin the old value in your `terraform.tfvars`
> (`app_name = "planning-poker"`) unless you actually want new resources.

### Azure — a single App Service

Deploy the published output to one App Service instance. The tier/size is a DevOps decision; it must
provide the three constraints above:

- **WebSockets: On** (Configuration → General settings).
- **Always On: On**.
- **Single instance — do not scale out.**

Create the site however you prefer (portal, `az` CLI, or your own IaC) — Linux, `DOTNETCORE|10.0` —
with the SQLite connection string pointed at persistent storage:

- App setting `ConnectionStrings__Default = Data Source=/home/data/teamtools.db`
- App setting `ASPNETCORE_ENVIRONMENT = Production`
- **B1** is the floor SKU (cheapest that supports WebSockets + Always On).

```bash
dotnet publish backend/src/TeamTools.Api -c Release -o ./publish
cd publish && zip -r ../app.zip . && cd ..
az webapp deploy -g <resource-group> -n <appName> --src-path app.zip --type zip
```

The app applies EF Core migrations and creates its SQLite directory automatically on startup, then
serves the REST API, both SignalR hubs, and the Angular SPA from the one site.

## License & attribution

**TeamTools is owned and maintained by Darren Horrocks**, and is open source under the
[Apache License 2.0](./LICENSE).

You are free to **use, run, modify, and contribute** to it — including commercially. In return, the
license requires that you **keep the attribution**:

- Retain the `LICENSE` and [`NOTICE`](./NOTICE) files (and the copyright notices) in any copy or fork.
- **Credit the project** — if you use, deploy, or build on it, you must visibly state that it's based
  on *TeamTools by Darren Horrocks* (see `NOTICE` for the wording and where it must appear).
- **Mark your changes** — modified files must carry a prominent notice saying you changed them.

Taking the source and shipping it — commercially or otherwise — **without that attribution is a breach
of the license**. Contributions are submitted under the same Apache 2.0 terms (inbound = outbound).
