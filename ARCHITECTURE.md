# Architecture

Design and architecture notes for **TeamTools** — a platform hosting two team-ceremony tools over one
shared room engine: **Planning Poker** (real-time estimation) and **Team Retro** (real-time
retrospectives).

Per-feature behaviour is tracked on the [GitHub issues board](https://github.com/bizzehdee/plnpkr/issues)
and in [plan.md](./plan.md) / [tasks.md](./tasks.md); this document covers the cross-cutting design — the
domain model, the real-time contract, project structure, testing strategy, and deployment — that isn't
tied to a single feature.

> **Status: this document describes the target, ahead of the code.** It is written first, deliberately
> (task #17), because tasks #18–#28 rename the solution, split the domain model in two and rewrite the
> persistence schema with hand-written data motion — a refactor that needs an agreed design to implement
> and to be reviewed against. The code catches up task by task; task #29 corrects this document wherever
> the implementation found the design wrong (and says so, rather than quietly rewriting history).
> Where the two differ today, the section notes it.

## Resolved foundational decisions

- **Real-time transport: SignalR** (over WebSockets) — handles reconnection and groups (one group per
  room), with a first-class Angular client (`@microsoft/signalr`).
- **Persistence: EF Core**, SQLite by default; the provider is configurable (SQLite / SQL Server /
  PostgreSQL). Rooms, participants and tool state survive restarts.
- **Auth: none — anonymous, name-only.** No accounts. A user supplies a display name; the name plus a
  stable per-browser `userId` live in browser `localStorage` and are pre-filled on return.
- **Versions: .NET 10 LTS + Angular 21 LTS.**
- **Deployment: a single host** (the .NET API serves the Angular SPA from the same origin).
- **Two tools, one room engine.** A room is created as *either* a poker session *or* a retro board and
  cannot switch. There is no team/workspace entity: the tools share the room engine and the front door,
  nothing else.

## The Room / tool-payload boundary

This is the central structural decision. Everything that is true of *any* ceremony room lives on `Room`;
everything specific to a ceremony lives on that tool's payload. A room owns exactly one payload.

The split is not cosmetic — it is what makes every cross-cutting feature already built apply to both
tools without a second implementation. Rate limiting, the accessibility baseline, i18n, large-group mode,
multi-organiser with succession, and the retention policy are all *room-level* concerns; see
[Inherited cross-cutting concerns](#inherited-cross-cutting-concerns).

### Where each current `Session` field goes

`Session` is split; no field is dropped.

| → `Room` (tool-agnostic) | → `PokerRound` (estimation-specific) |
| --- | --- |
| `Id`, `ShortCode`, `Name` | `State` (Voting / Revealed / Discussion) |
| `Tool` *(new discriminator)* | `DeckType`, `CustomCards` |
| `OrganiserUserId` / organiser set | `CurrentStory`, `CurrentStoryNote` |
| `PasswordHash` | `AutoReveal` |
| `ReactionsEnabled`, `AllowRoleChange` | `TimerDurationSeconds`, `TimerDeadline`, `TimerPausedRemainingSeconds` |
| `CreatedAt`, `LastActivityAt` | `LinkedProvider`, `LinkedIssue`, `TicketQueue` |
| `ClosedAt`, `DeletedAt` | `RoundResults` (completed-round history) |
| `Participants` | |

Two judgement calls worth stating, because they are the ones a reader would otherwise question:

- **`AllowRoleChange` and roles stay room-level.** Voter/Observer is meaningful in a retro too (an
  observer watches without adding cards or spending dots), so the role model is shared rather than
  duplicated per tool.
- **The round timer moves to `PokerRound`, but the *timer service* becomes room-level.** The
  deadline-broadcast mechanism is reused verbatim by the retro phase countdown; only the persisted
  duration/deadline fields are per-tool.

### Domain model

```
Room                          # the shared engine
  Id (Guid)
  ShortCode                   # URL-friendly invite slug, e.g. "blue-fox-42"
  Name                        # creator-chosen
  Tool: Poker | Retro         # fixed at creation, immutable thereafter
  PasswordHash?               # PBKDF2 hash of an optional join password (never plaintext)
  ReactionsEnabled, AllowRoleChange (bool)
  ClosedAt?, DeletedAt?       # read-only / soft-delete lifecycle timestamps
  CreatedAt, LastActivityAt
  Participants: [Participant]
  PokerRound?                 # exactly one payload is non-null, per Tool
  RetroBoard?

Participant                   # shared by both tools
  UserId                      # stable per browser; survives reconnect
  DisplayName, NormalizedName # unique per room (case-insensitive)
  IsOrganiser (bool)          # a set, not a single organiser (#7); auto-succession on disconnect
  Role: Voter | Observer
  Vote?                       # poker only; hidden until revealed; always null for Observers
  HasVoted, ChangedAfterReveal (bool)
  IsConnected, LastSeenAt     # disconnect/reconnect tracking

PokerRound                    # 1:1 with a Poker room
  State: Voting | Revealed | Discussion   # Revealed is NOT a lock — votes can still change/clear
  DeckType (enum), CustomCards?
  Cards (string[])            # resolved server-side from DeckType (or custom)
  AutoReveal (bool)
  CurrentStory?, CurrentStoryNote?
  TimerDurationSeconds?, TimerDeadline?, TimerPausedRemainingSeconds?
  LinkedProvider?, LinkedIssue?, TicketQueue   # issue-tracker integration (broadcast-safe only)
  RoundResults: [RoundResult] # completed-round history for analytics/export

RetroBoard                    # 1:1 with a Retro room
  Phase: Collect | Group | Vote | Discuss | Actions | Closed
  TemplateType (enum), Columns: [RetroColumn]
  Anonymous (bool)            # locked once the first card exists — see below
  VoteBudget (int), AllowMultiplePerItem (bool)
  AllowParticipantGrouping (bool)
  PhaseDeadline?              # same deadline-broadcast mechanism as the poker round timer
  PreviousBoardShortCode?     # provenance for carried-over actions
  Cards: [RetroCard], Groups: [RetroGroup], Votes: [RetroVote], Actions: [RetroActionItem]

RetroColumn        Id, BoardId, Title, Order
RetroCard          Id, ColumnId, GroupId?, AuthorUserId, Text, CreatedAt, Order
RetroGroup         Id, BoardId, Label, Order
RetroVote          BoardId, VoterUserId, TargetKind: Card | Group, TargetId
RetroActionItem    Id, BoardId, Title, OwnerUserId?, OwnerName?, DueDate?, DoneAt?,
                   SourceGroupId?, CarriedFromBoardId?
```

Card decks (Sequential, Fibonacci, Modified Fibonacci, T-shirt, Powers of two, Custom) are resolved
server-side from `DeckType` so all clients agree; every deck also appends `?` (unsure) and `☕` (break).
Stats (average/consensus/outliers) are computed over numeric votes only. Retro column templates (Went
well / To improve / Actions, Start-Stop-Continue, 4Ls, Mad-Sad-Glad, Custom) resolve the same way, from
`TemplateType` in a `RetroTemplateCatalog` that mirrors `DeckCatalog`.

### Anonymity is a contract property, not a UI setting

`RetroBoard.Anonymous` changes **what goes on the wire**, not what the client chooses to render. A
client-side-only hide is a promise that one devtools panel disproves.

- Under anonymity the snapshot carries no `AuthorUserId` for anyone but the recipient; each card is
  marked `IsMine` so an author can still edit their own.
- `AuthorUserId` is still *stored* — an author must be able to edit their card and organiser moderation
  needs a target. So this is anonymity **from participants, not from a database administrator**, and the
  UI must not imply otherwise.
- The toggle locks once the first card exists. Flipping it mid-retro would retroactively expose cards
  written under a promise of anonymity.
- **Exports honour it too**: an anonymous board exports no author column in any format, with no organiser
  override.

The mechanism already exists — poker projects the snapshot per recipient to hide votes before reveal —
so this is the same projection applied to a second field.

## Persistence

### Table shape after the split

```
Rooms          # renamed from Sessions, minus the poker columns, plus Tool
PokerRounds    # 1:1 with a Poker room; owns the moved poker columns (incl. the owned LinkedIssue
               #   columns and the TicketQueue JSON column)
Participants   # unchanged except SessionId -> RoomId
RoundResults   # unchanged except SessionId -> RoomId (via PokerRounds)
RetroBoards, RetroColumns, RetroCards, RetroGroups, RetroVotes, RetroActionItems
```

Preserved from the current model: the unique index on `Rooms.ShortCode`; the two per-room unique indexes
on `Participants` (`(RoomId, NormalizedName)` and `(RoomId, UserId)`); cascade delete from a room to its
participants, payload and history; and the global soft-delete query filter (`DeletedAt == null`), which
retention queries must bypass explicitly (`IgnoreQueryFilters()`).

### The migration must move data, not drop it

The refactor migration (task #19) is the one place in this project where a scaffolded migration is
actively dangerous: EF's default answer to a table split is drop-and-recreate, which would silently
discard every existing session, participant, story note and round-history row.

**Requirement:** the data motion is hand-written for all three providers — rename `Sessions` → `Rooms`,
create `PokerRounds`, `INSERT … SELECT` the poker columns across keyed by room id, then drop the moved
columns. Verified by upgrading a pre-refactor database and asserting rooms, participants, notes and round
history all survive.

## Real-time contract (SignalR)

Two hubs, one shape. One group per room, keyed by short code. A hub is a **thin adapter**: each method
authenticates the caller's `userId`, calls a service method, and broadcasts the result. No business logic
lives in a hub.

```
PokerHub    (renamed from PlanningPokerHub)   RetroHub
   ↘                                            ↙
        RoomService  (join/leave/role/organiser/password/close/delete)
   ↙                                            ↘
   PokerService                                 RetroService
```

- **Shared `RoomSnapshot` fragment.** Each tool broadcasts its own full snapshot after every
  mutation — `PokerSessionUpdated` / `RetroBoardUpdated` — and both embed the same `RoomSnapshot`
  (short code, name, tool, participants with presence/roles/organiser flags, closed state). Room-level
  fields are defined once and never duplicated per tool.
- **Shared events.** Ephemeral `ReactionReceived` (never persisted) and terminal `RoomClosed` are
  common to both tools.
- **Client → server (poker):** create/join/leave; cast vote; reveal / reset-one / reset-all; set
  auto-reveal, story, story note, deck, password, reactions-enabled, allow-role-change; change role;
  promote/demote/transfer organiser; round-timer start/pause/resume/stop/set-duration; start/end
  discussion; close/delete room; emoji `React`; issue-tracker connect/disconnect/link/submit-points/queue.
- **Client → server (retro):** create/join/leave; add/edit/delete/move card; set template; set
  anonymous; advance/set phase; group/ungroup/rename group; cast/withdraw dot vote; add/edit/delete
  action, toggle done; close/delete room; emoji `React`.
- **Per-recipient projection.** Both tools hide state that must not leak: poker hides vote values before
  reveal; retro hides other participants' card text during Collect, authorship on an anonymous board,
  and dot totals during Vote. This is done in the snapshot projection, never in the client.
- **Reconnection:** the client stores `userId` in `localStorage`; on reconnect the server re-attaches the
  existing participant by `userId` (not connection id), reclaiming their seat, vote/dots, role and
  organiser status.

### One deliberate cross-board link

Retro **carry-over** (task #27) is the single connection between two rooms: a new board can pull the
unfinished action items from a previous board's short code. It **copies** them (keeping
`CarriedFromBoardId` for provenance) rather than referencing them, so the new board stays self-contained
for export and is unaffected when the old one is retention-deleted. Because a short code is effectively a
bearer token, carry-over requires the source board's password if it had one — otherwise it would be a
read hole in a protected board.

## Backend structure

The solution is split for testability — all decision-making lives in pure, dependency-free domain
projects; `Api` and `Data` are thin adapters.

```
/backend
  TeamTools.Core/             # Pure, tool-agnostic room engine. No framework deps.
    Models/                   # Room, Participant, enums, value objects
    RoomService.cs            # join, name-uniqueness, roles, organiser set + succession,
                              #   password, presence, close/delete
    RoomMaintenanceService.cs # participant eviction + retention decisions (clock-driven)
    IRoomStore.cs             # persistence abstraction (no EF types leak through it)
    IClock.cs                 # time abstraction for deterministic time-based tests
    Security/                 # PasswordHasher (PBKDF2)
    Integrations/             # provider-agnostic issue-tracker ports (IIssueTracker, etc.)
  TeamTools.Poker/            # Estimation domain: PokerService, DeckCatalog, StatsCalculator,
                              #   PokerRound state machine, round timer, discussion phase, RoundResult
  TeamTools.Retro/            # Retrospective domain: RetroService, RetroTemplateCatalog,
                              #   phase state machine, grouping, dot-vote tallies, action items
  TeamTools.Data/             # EF Core adapter: TeamToolsDbContext, EfRoomStore, IDatabaseProvider
  TeamTools.Data.{Sqlite,SqlServer,PostgreSql}/   # one project per engine: driver + migrations
  TeamTools.Integrations/     # Jira/ADO/GitHub/GitLab HTTP adapters, OAuth flow, sanitizer, allowlist
  TeamTools.Api/              # Host, thin PokerHub + RetroHub + MVC controllers + health checks
```

Key choices:
- **Logic in the domain projects, not in the hubs/controllers.** Adapters call a service method and
  broadcast the result. Every rule is under fast unit tests.
- **Services return explicit result objects** (e.g. `JoinResult.Ok(snapshot)` / `NameTaken`) rather than
  calling transport back. Tests assert on returned outcomes.
- **`IRoomStore`** hides EF Core; `Core.Tests` use an in-memory fake, `Data.Tests` verify the real
  `EfRoomStore` against SQLite. **`IClock`** makes time-based behaviour (eviction, retention, timer and
  phase expiry) deterministic.
- **`TeamTools.Poker` and `TeamTools.Retro` depend on `TeamTools.Core`, never on each other.** A
  dependency between the two tools is the failure mode this structure exists to prevent; a third tool
  should need no change to either.
- **Engine-per-project:** `TeamTools.Data` holds only the model + `DbContext` + the `IDatabaseProvider`
  abstraction (no driver). Each engine project owns its driver and provider-specific migrations, so a
  build only ships the engines it references. Provider is chosen by config (`Database:Provider`);
  unknown/empty ⇒ SQLite.

### Inherited cross-cutting concerns

These are implemented once, at room level, and apply to **both** tools by construction. Anything added
here later must work for both.

| Concern | Where it lives | Applies to retro as |
| --- | --- | --- |
| Rate limiting / abuse (#3) | room-scoped token buckets + REST middleware; max participants | create/join limits; card-add throttling |
| Accessibility baseline (#4) | frontend conventions: roving-tabindex groups, `aria-live`, focus trap | keyboard card entry, phase announcements, a keyboard equivalent for every drag |
| i18n (#5) | runtime catalogs (en/es/pt/pl), `Intl` number/plural/date formatting | template + phase names, dot budgets, action due dates |
| Large-group mode (#6) | snapshot payload discipline, virtualized participant list | boards with many cards use the same full-snapshot broadcast, already proven at 50+ |
| Multi-organiser + succession (#7) | organiser set on `Participant`, longest-connected succession | facilitator hand-off mid-retro |
| Retention (#15) | `RoomMaintenanceService` + `RetentionOptions`, surfaced via `GET /api/config` | identical windows for retro boards |

**One deliberate exception to the closed-room rule:** action items stay editable after a room is closed,
because "mark done" happens days after the retro. That is an explicit carve-out in the close check —
narrow, tested, and the only write permitted on a closed room.

## Frontend structure

```
/frontend/src/app/
  core/      room.client.ts   (shared room-level realtime behind IRealtimeClient)
             poker.client.ts, retro.client.ts (per-tool events)
             state via signals, pure reducers for incoming events,
             localStorage services (identity, theme, decks, tracker), i18n service
  pages/     home     (platform tool picker)
             poker/   (create, table)
             retro/   (create, board)
             join     (resolves a short code -> { tool, shortCode } and routes accordingly)
```

Routes are `/`, `/join/:shortCode`, `/poker/:shortCode`, `/retro/:shortCode` plus per-tool create routes.
`/session/:shortCode` is kept as a **permanent redirect** to `/poker/:shortCode` so invite links already
sitting in people's calendars keep working.

SignalR sits behind an `IRealtimeClient` interface so components/stores test against a fake (no live
socket). Incoming events apply through **pure reducer functions** (event + state → new state), unit-tested
directly; component specs (Angular TestBed + fake client) assert user-visible behaviour. Zoneless,
standalone components with Angular signals; Bootstrap 5 with native color modes for theming.

## Testing strategy

**Test behaviour, not implementation.** Tests assert observable outcomes (resulting state, returned
values/events, what a user sees) — never internal call sequences or private members. Mocks only at true
boundaries (the store, the clock, the realtime transport).

- **`TeamTools.Core.Tests`** — the room engine: `RoomService` through its public API against the
  in-memory store + fake clock; retention/eviction decisions.
- **`TeamTools.Poker.Tests`** — `PokerService` state machine, `DeckCatalog` / `StatsCalculator`
  (table-driven), round timer and discussion phase.
- **`TeamTools.Retro.Tests`** — `RetroService`: phase transitions and phase-gated mutations, grouping,
  server-side dot budgets, action items, carry-over. **Snapshot-projection tests are first-class here:**
  assert that an anonymous board's snapshot carries no other-author identity *on the wire*, that Collect
  hides others' card text, and that dot totals are absent during Vote.
- **`TeamTools.Data.Tests`** — `EfRoomStore` behaviour against real SQLite (unique constraints, cascade
  delete, query filter, projected queries) **plus a pre-refactor-database upgrade test** for the #19
  migration.
- **`TeamTools.Integrations.Tests`** — tracker adapters against a stubbed `HttpMessageHandler`.
- **`TeamTools.Api.Tests`** — hub/controller/health behaviour via `WebApplicationFactory` + a SignalR
  test client; kept thin (logic is already covered in the domain test projects).
- **Frontend** — pure reducers/formatting unit-tested directly; component specs via TestBed + fake client.

**Coverage:** the existing hard gate of **≥90% line and branch** carries over to the domain projects —
`TeamTools.Core`, `TeamTools.Poker` and `TeamTools.Retro` (`coverage-gate.ps1`) — with lighter
expectations on adapter/wiring projects. Generated code (EF migrations, Angular boilerplate) is excluded.
Coverage is a guardrail; every test maps to a behaviour. The gate is also the safety net for the #19
refactor, which must land with the suite green and no behavioural change.

## Deployment & hosting

One deployable unit: `dotnet publish` builds the Angular app into the API's `wwwroot` and serves it via
`UseDefaultFiles()` + `UseStaticFiles()` with a SPA fallback (deep links like `/join/blue-fox-42` return
`index.html`). One origin → no CORS. The API and SPA can also be built and deployed separately (the SPA
reads its API base from a runtime `config.js`).

**Health checks:** `/health` is a readiness probe (DB reachable + queryable → 200/503); `/health/live` is
a liveness probe. Both return a small JSON breakdown. **`GET /api/config`** exposes the small server-config
surface the client needs (retention windows today).

**Single instance by design.** SignalR uses the in-process backplane and SQLite is a local file, so **do
not scale out** — scale up. Horizontal scale-out is a future path (Azure SignalR Service / Redis backplane
+ a server database). When self-hosting on a single instance, the platform must provide WebSockets and
keep the process alive (no idle shutdown), and the SQLite connection string should point at persistent
storage; the app creates the directory and runs migrations (and enables WAL) on startup.

**Database file rename.** The default SQLite filename changes with the rename (task #18). An existing
`planningpoker.db` is still read if present, with a log hint to rename it, rather than the app silently
starting against an empty database.

**Docker:** a multi-stage `Dockerfile` + `docker-compose.yml` produce one container serving the API, both
hubs, and the SPA as a non-root user with SQLite on a volume.

## License

Apache License 2.0 with attribution — see [`LICENSE`](./LICENSE) and [`NOTICE`](./NOTICE).
