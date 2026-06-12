# Architecture

Design and architecture notes for **plnpkr**. Per-feature behaviour is tracked on the
[GitHub issues board](https://github.com/bizzehdee/plnpkr/issues) (the single source of truth for
features); this document covers the cross-cutting design — the domain model, the real-time contract,
project structure, testing strategy, and deployment — that isn't tied to a single feature.

## Resolved foundational decisions

- **Real-time transport: SignalR** (over WebSockets) — handles reconnection and groups (one group per
  session), with a first-class Angular client (`@microsoft/signalr`).
- **Persistence: EF Core**, SQLite by default; the provider is configurable (SQLite / SQL Server /
  PostgreSQL). Sessions and participants survive restarts.
- **Auth: none — anonymous, name-only.** No accounts. A user supplies a display name; the name plus a
  stable per-browser `userId` live in browser `localStorage` and are pre-filled on return.
- **Versions: .NET 10 LTS + Angular 21 LTS.**
- **Deployment: a single host** (the .NET API serves the Angular SPA from the same origin).

## Domain model

```
Session
  Id (Guid)
  ShortCode            # URL-friendly invite slug, e.g. "blue-fox-42"
  Name                 # organiser-chosen
  DeckType (enum)      # which card set
  Cards (string[])     # resolved server-side from DeckType (or custom)
  State: Voting | Revealed   # Revealed is NOT a lock — votes can still change/clear
  OrganiserUserId?     # the organiser's userId, or null if the session has no organiser
  AutoReveal (bool)    # reveal once all voters have voted
  ReactionsEnabled, AllowRoleChange (bool)
  CurrentStory?
  TimerDurationSeconds?, TimerDeadline?, TimerPausedRemainingSeconds?   # round timer
  PasswordHash?        # PBKDF2 hash of an optional join password (never plaintext)
  LinkedProvider?, LinkedIssue?, TicketQueue   # issue-tracker integration (broadcast-safe only)
  ClosedAt?, DeletedAt?   # read-only / soft-delete lifecycle timestamps
  CreatedAt, LastActivityAt
  Participants: [Participant]

Participant
  UserId               # stable per browser; survives reconnect
  DisplayName, NormalizedName   # unique per session (case-insensitive)
  IsOrganiser (bool)
  Role: Voter | Observer
  Vote?                # hidden until revealed; always null for Observers
  HasVoted (bool)
  ChangedAfterReveal (bool)   # vote set/changed while Revealed
  IsConnected, LastSeenAt     # disconnect/reconnect tracking
```

Card decks (Sequential, Fibonacci, Modified Fibonacci, T-shirt, Powers of two, Custom) are resolved
server-side from `DeckType` so all clients agree; every deck also appends `?` (unsure) and `☕` (break).
Stats (average/consensus/outliers) are computed over numeric votes only.

## Real-time contract (SignalR hub)

One group per session, keyed by short code. The hub is a **thin adapter**: each method authenticates the
caller's `userId`, calls a `SessionService` method, and broadcasts the result. No business logic lives in
the hub.

- **Client → server:** create/join/leave; cast vote; reveal / reset-one / reset-all; set auto-reveal,
  story, deck, password, reactions-enabled, allow-role-change; change role; round-timer
  start/pause/resume/stop/set-duration; close/delete session; emoji `React`; issue-tracker
  connect/disconnect/link/submit-points/queue.
- **Server → client:** primarily a full `SessionUpdated` snapshot broadcast after every mutation, plus
  transient `ReactionReceived` (ephemeral, never persisted) and `SessionClosed`.
- **Reconnection:** the client stores `userId` in `localStorage`; on reconnect the server re-attaches the
  existing participant by `userId` (not connection id), reclaiming their vote/role/organiser slot.

## Backend structure

The solution is split for testability — all decision-making lives in a pure, dependency-free `Core`
project; `Api` and `Data` are thin adapters.

```
/backend
  PlanningPoker.Core/         # Pure domain — no framework deps. The main unit-test target.
    Models/                   # Session, Participant, enums, value objects
    DeckCatalog.cs            # DeckType -> card list (pure)
    StatsCalculator.cs        # average/consensus/outliers (pure)
    SessionService.cs         # all rules: join, name-uniqueness, vote lifecycle, reveal,
                              #   auto-reveal gate, organiser permissions, resets, timer, lifecycle
    SessionMaintenanceService.cs  # idle eviction + round-timer expiry decisions (clock-driven)
    ISessionStore.cs          # persistence abstraction (no EF types leak through it)
    IClock.cs                 # time abstraction for deterministic time-based tests
    Integrations/             # provider-agnostic issue-tracker ports (IIssueTracker, etc.)
  PlanningPoker.Data/         # EF Core adapter: PlanningPokerDbContext, EfSessionStore, IDatabaseProvider
  PlanningPoker.Data.{Sqlite,SqlServer,PostgreSql}/   # one project per engine: driver + migrations
  PlanningPoker.Integrations/ # Jira/ADO HTTP adapters, OAuth flow, HTML sanitizer, host allowlist
  PlanningPoker.Api/          # Host, thin SignalR hub + MVC controllers + health checks
```

Key choices:
- **Logic in `Core`, not in the hub/controllers.** Adapters call a `SessionService` method and broadcast
  the result. Every rule is under fast unit tests.
- **`SessionService` returns explicit result objects** (e.g. `JoinResult.Ok(snapshot)` / `NameTaken`)
  rather than calling transport back. Tests assert on returned outcomes.
- **`ISessionStore`** hides EF Core; `Core.Tests` use an in-memory fake, `Data.Tests` verify the real
  `EfSessionStore` against SQLite. **`IClock`** makes time-based behaviour (eviction, timer expiry)
  deterministic.
- **Engine-per-project:** `PlanningPoker.Data` holds only the model + `DbContext` + the
  `IDatabaseProvider` abstraction (no driver). Each engine project owns its driver and provider-specific
  migrations, so a build only ships the engines it references. Provider is chosen by config
  (`Database:Provider`); unknown/empty ⇒ SQLite.

## Frontend structure

```
/frontend/src/app/
  core/      realtime.client.ts (SignalR behind IRealtimeClient), session state via signals,
             pure reducers for incoming events, localStorage services (identity, theme, decks, tracker)
  pages/     home (create/join), session (the poker table)
```

SignalR sits behind an `IRealtimeClient` interface so components/stores test against a fake (no live
socket). Incoming events apply through **pure reducer functions** (event + state → new state), unit-tested
directly; component specs (Angular TestBed + fake client) assert user-visible behaviour. Zoneless,
standalone components with Angular signals; Bootstrap 5 with native color modes for theming.

## Testing strategy

**Test behaviour, not implementation.** Tests assert observable outcomes (resulting state, returned
values/events, what a user sees) — never internal call sequences or private members. Mocks only at true
boundaries (the store, the clock, the realtime transport).

- **`PlanningPoker.Core.Tests`** — the workhorse: drives `SessionService` through its public API against
  the in-memory store + fake clock. `DeckCatalog` / `StatsCalculator` get table-driven tests.
- **`PlanningPoker.Data.Tests`** — `EfSessionStore` behaviour against real SQLite (unique constraint,
  cascade delete, query filter, projected queries).
- **`PlanningPoker.Integrations.Tests`** — Jira/ADO adapters against a stubbed `HttpMessageHandler`.
- **`PlanningPoker.Api.Tests`** — hub/controller/health behaviour via `WebApplicationFactory` + a SignalR
  test client; kept thin (logic is already covered in `Core.Tests`).
- **Frontend** — pure reducers/formatting unit-tested directly; component specs via TestBed + fake client.

**Coverage:** a hard gate of **≥90% line and branch on `PlanningPoker.Core`** (`coverage-gate.ps1`),
lighter expectations on adapter/wiring projects. Generated code (EF migrations, Angular boilerplate) is
excluded. Coverage is a guardrail; every test maps to a behaviour.

## Deployment & hosting

One deployable unit: `dotnet publish` builds the Angular app into the API's `wwwroot` and serves it via
`UseDefaultFiles()` + `UseStaticFiles()` with a SPA fallback (deep links like `/join/blue-fox-42` return
`index.html`). One origin → no CORS. The API and SPA can also be built and deployed separately (the SPA
reads its API base from a runtime `config.js`).

**Health checks:** `/health` is a readiness probe (DB reachable + queryable → 200/503); `/health/live` is
a liveness probe. Both return a small JSON breakdown.

**Single instance by design.** SignalR uses the in-process backplane and SQLite is a local file, so **do
not scale out** — scale up. Horizontal scale-out is a future path (Azure SignalR Service / Redis backplane
+ a server database). When self-hosting on a single instance, the platform must provide WebSockets and
keep the process alive (no idle shutdown), and the SQLite connection string should point at persistent
storage; the app creates the directory and runs migrations (and enables WAL) on startup.

**Docker:** a multi-stage `Dockerfile` + `docker-compose.yml` produce one container serving the API, hub,
and SPA as a non-root user with SQLite on a volume.

## License

Apache License 2.0 with attribution — see [`LICENSE`](./LICENSE) and [`NOTICE`](./NOTICE).
