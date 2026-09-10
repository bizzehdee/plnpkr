# TeamTools

**TeamTools** is a small platform for the ceremonies a team actually runs together, in real time and
without accounts. It hosts four tools over one shared room engine:

- **Planning Poker** — real-time Scrum estimation.
- **Team Retro** — real-time retrospectives, from writing cards to agreeing actions.
- **Lean Coffee** — an agenda-less discussion: propose topics, vote, then work the list.
- **Async Standup** — the standup without the meeting: post in your own time, read the rest once you have.

All four work the same way: someone creates a room, shares a short invite link, and everyone else joins
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

## Lean Coffee

- **Propose → Vote → Discuss → Done**, on the same one-step-at-a-time phase rail the retro uses.
- **Hidden proposal:** during Propose you see your own topics and a count of how many others are
  being written, so nobody writes "same as Ada's" instead of their own thought.
- **Dot voting** with the same server-enforced budget as the retro; totals stay hidden while voting
  is open, then become the ranked agenda.
- **A timebox per topic**, not per phase — the countdown restarts for each one.
- **A keep-going vote when time runs out.** Answers stay hidden until the facilitator closes the
  vote, so the room does not just follow whoever clicked first; a majority to keep going restarts
  the clock and counts an extension, a tie moves on.
- **The discussion log is the output:** how long each topic actually got, how many extensions it
  took, and the decisions recorded against it — with an owner and a due date, editable after the
  session closes.
- Topics are **never anonymous**: proposing one means offering to talk about it, so the name is the
  useful part.

## Async Standup

- **Post-to-read.** You see everyone else's answers once you have posted your own. Enforced in the
  per-recipient snapshot, not the UI — a standup you read first is a standup you write to match.
- **Your own questions.** The usual three are the starting point, not the law; a room can ask up to
  six of whatever it actually asks.
- **Blockers are the only structured field**, because "who is unblocking this" is the only decision a
  standup produces. Anyone can take one on, the owner need not be in the room, and it stays clearable
  after the standup closes — it gets unblocked hours later, not during.
- **Each day is its own room.** Start today's from yesterday's short code to copy the questions and
  anything still in the way; the invite link *is* the reminder.
- **A count, never a name.** The board says "3 of the 5 people in this room have posted" and never
  "Dave is missing" — presence only knows who opened the room, so naming the absent would name the
  wrong people.
- **Export it or lose it**, stated on the board: idle rooms are evicted like any other, and a standup
  room is idle by construction between mornings. The Markdown and CSV are rendered from the snapshot
  you already hold, so the file is exactly what you were allowed to read.

> **What this tool deliberately does not have:** no phase rail (a standup opens, people post, it
> closes), no countdown, no recurrence, no notifications and no roster. Those would need a scheduler,
> addresses or accounts — platform decisions that would change all four tools, and worth taking
> deliberately and once rather than as a workaround for one.

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

One container serves the API, every SignalR hub, and the Angular SPA (the same single-artifact shape
as the production build below). SQLite is stored on a named volume so it survives restarts.

```bash
docker compose up --build      # → http://localhost:8080
```

The multi-stage [`Dockerfile`](./Dockerfile) builds the SPA and publishes the API; the image runs as a
non-root user. Configuration is passed as environment variables (e.g. `Integrations__Jira__Enabled=true` in
[`docker-compose.yml`](./docker-compose.yml); OAuth client id/secret via env, never baked in).

> Single instance only — in-process SignalR + local SQLite means **don't run multiple replicas** of
> this image as-is (scale up, not out).

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
  REST and every SignalR hub derive their URL from this. Leave it `""` for the same-origin
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
serves the REST API, every SignalR hub, and the Angular SPA from the one site.

## License & attribution

**TeamTools is owned and maintained by Darren Horrocks**, and is open source under the
[Apache License 2.0](./LICENSE).

You are free to **use, run, modify, and contribute** to it — including commercially. In return, the
license requires that you **keep the attribution**:

- Retain the `LICENSE` and [`NOTICE`](./NOTICE) files (and the copyright notices) in any copy or fork.
- **Credit the project** — if you use, deploy, or build on it, you must visibly state that it's based
  on *TeamTools by Darren Horrocks* (see `NOTICE` for the wording and where it must appear).
- **Mark your changes** — modified files must carry a prominent notice saying you changed them.

Taking the source and shipping it, commercially or otherwise, **without that attribution is a breach
of the license**. Contributions are submitted under the same Apache 2.0 terms (inbound = outbound).
