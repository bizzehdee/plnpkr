# Architecture

Design and architecture notes for **TeamTools** — a platform hosting four team-ceremony tools over
one shared room engine: **Planning Poker** (real-time estimation), **Team Retro** (real-time
retrospectives), **Lean Coffee** (agenda-less discussion) and **Async Standup** (post-to-read, no
meeting).

Per-feature behaviour is tracked on the [GitHub issues board](https://github.com/bizzehdee/plnpkr/issues)
and in [plan.md](./plan.md) / [tasks.md](./tasks.md); this document covers the cross-cutting design — the
domain model, the real-time contract, project structure, testing strategy, and deployment — that isn't
tied to a single feature.

> **Status: the code has caught up.** This document was written first, deliberately (task #17),
> because tasks #18–#28 renamed the solution, split the domain model in two and rewrote the
> persistence schema with hand-written data motion — a refactor that needed an agreed design to
> implement against and to be reviewed against. Those tasks have landed, and task #29 has corrected
> this document where the implementation found the design wrong. Every such place is marked
> **Design correction** and says what changed and why, rather than quietly rewriting history.

## Resolved foundational decisions

- **Real-time transport: SignalR** (over WebSockets) — handles reconnection and groups (one group per
  room), with a first-class Angular client (`@microsoft/signalr`).
- **Persistence: EF Core**, SQLite by default; the provider is configurable (SQLite / SQL Server /
  PostgreSQL). Rooms, participants and tool state survive restarts.
- **Auth: none — anonymous, name-only.** No accounts. A user supplies a display name; the name plus a
  stable per-browser `userId` live in browser `localStorage` and are pre-filled on return.
- **Versions: .NET 10 LTS + Angular 21 LTS.**
- **Deployment: a single host** (the .NET API serves the Angular SPA from the same origin).
- **Four tools, one room engine.** A room is created as exactly one of a poker session, a retro board, a Lean Coffee or a standup, and
  cannot switch. There is no team/workspace entity: the tools share the room engine and the front door,
  nothing else.

## The Room / tool-payload boundary

This is the central structural decision. Everything that is true of *any* ceremony room lives on `Room`;
everything specific to a ceremony lives on that tool's payload. A room owns exactly one payload.

The split is not cosmetic — it is what makes every cross-cutting feature already built apply to both
tools without a second implementation. Rate limiting, the accessibility baseline, i18n, large-group mode,
multi-organiser with succession, and the retention policy are all *room-level* concerns; see
[Inherited cross-cutting concerns](#inherited-cross-cutting-concerns).

### Where each old `Session` field went

`Session` was split; no field was dropped.

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

Participant                   # shared by every tool
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
  Template (enum), Columns: [RetroColumn]
  Anonymous (bool)            # locked once the first card exists — see below
  VoteBudget (int), AllowMultiplePerItem (bool)
  AllowParticipantGrouping (bool)
  PhaseDurationSeconds?, PhaseDeadline?   # same deadline-broadcast mechanism as the poker round timer
  PreviousBoardShortCode?     # provenance for carried-over actions
  Cards: [RetroCard], Groups: [RetroGroup], Votes: [RetroVote], Actions: [RetroActionItem]

RetroColumn        Id, BoardId, Title, Order
RetroCard          Id, ColumnId, GroupId?, AuthorUserId, Text, CreatedAt, Order
RetroGroup         Id, BoardId, Label, Order
RetroVote          Id, BoardId, VoterUserId, TargetKind: Card | Group, TargetId
RetroActionItem    Id, BoardId, Title, OwnerUserId?, OwnerName?, DueDate?, DoneAt?,
                   SourceGroupId?, CarriedFromBoardId?
```

Card decks (Sequential, Fibonacci, Modified Fibonacci, T-shirt, Powers of two, Custom) are resolved
server-side from `DeckType` so all clients agree; every deck also appends `?` (unsure) and `☕` (break).
Stats (average/consensus/outliers) are computed over numeric votes only. Retro column templates (Went
well / To improve / Actions, Start-Stop-Continue, 4Ls, Mad-Sad-Glad, Custom) resolve the same way, from
`Template` in a `RetroTemplateCatalog` that mirrors `DeckCatalog`.

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
- **Exports honour it too**: an anonymous board exports no author column in any format — the CSV drops
  the column entirely rather than blanking it — and there is no organiser override, because
  `RetroService.GetExportAsync` takes no caller identity at all. There is nothing to authorise, so
  there is nothing to get wrong.

The mechanism already exists — poker projects the snapshot per recipient to hide votes before reveal —
so this is the same projection applied to a second field.

## Persistence

### Table shape after the split

```
Rooms            # replaces Sessions, minus the poker columns, plus Tool
PokerRounds      # 1:1 with a Poker room; owns the moved poker columns (incl. the owned LinkedIssue
                 #   columns and the TicketQueue JSON column)
Participants     # unchanged except SessionId -> RoomId
RoundResult      # unchanged except SessionId -> RoomId (via PokerRounds)
RetroBoard, RetroColumn, RetroCard, RetroGroup, RetroVote, RetroActionItem
```

Preserved from the pre-split model: the unique index on `Rooms.ShortCode`; the two per-room unique
indexes on `Participants` (`(RoomId, NormalizedName)` and `(RoomId, UserId)`); cascade delete from a room
to its participants, payload and history; and the global soft-delete query filter (`DeletedAt == null`),
which retention queries must bypass explicitly (`IgnoreQueryFilters()`).

> **Design correction (#19–#28).** Child tables are named in the singular (`RoundResult`,
> `RetroCard`, …) because that is what EF's conventions produced for the existing schema and the new
> tables followed the schema already in the database rather than this document's plural. Only the two
> aggregate roots are plural. Not worth a rename migration to make prettier.

Two constraint choices the retro tables forced, both about cascade paths rather than aesthetics:

- **`RetroGroup` → `RetroCard` is `SetNull`, not cascade.** Deleting a theme must un-group its cards,
  not delete the team's input.
- **`RetroColumn` → `RetroCard` is `NoAction`.** A card is reachable from its board via both its column
  and the board itself, and SQL Server refuses multiple cascade paths to one table. The board's cascade
  is the one that matters, so the column's is explicit no-action.

### The migration must move data, not drop it

The refactor migration (task #19) is the one place in this project where a scaffolded migration is
actively dangerous: EF's default answer to a table split is drop-and-recreate, which would silently
discard every existing session, participant, story note and round-history row.

**Requirement:** the data motion is hand-written for all three providers — create `Rooms` and
`PokerRounds`, `INSERT … SELECT` the columns across from `Sessions` keyed by room id (stamping
`Tool = 'Poker'`), re-point the child tables, and only then drop `Sessions`. Verified by upgrading a
pre-refactor database and asserting rooms, participants, votes, notes and round history all survive,
plus a lossless downgrade.

> **Design correction (found while implementing #19): "re-point the child tables" cannot be left to
> EF on SQLite.** SQLite has no `ALTER TABLE … DROP CONSTRAINT`, so EF implements a foreign-key change
> as a *table rebuild* — and it defers that rebuild to the **end** of the migration while hoisting the
> `DROP TABLE "Sessions"` above it. With foreign keys enforced, dropping `Sessions` at that point fires
> the children's `ON DELETE CASCADE` and takes every `Participant` and `RoundResult` row with it. (EF's
> own `PRAGMA foreign_keys = 0` does not help: it is a no-op inside the migration's transaction.) The
> SQLite migration therefore rebuilds `Participants` and `RoundResult` itself, in raw SQL and in order,
> and drops `Sessions` last. The upgrade test caught this; reading the generated diff did not.

> **Design correction (#23, #25): string-enum and defaulted columns need their defaults set by hand.**
> A scaffolded `Phase` column defaulted to `""`, which is not a parseable `RetroPhase` and would have
> broken every board created before the column existed; `VoteBudget` defaulted to `0`, which is a board
> nobody can vote on. Both are hand-set (`'Collect'`, `3`) in all three providers. Any future column
> whose zero value is not a valid state needs the same treatment.

## Real-time contract (SignalR)

Four hubs, one shape — one per tool. One group per room, keyed by short code. A hub is a **thin adapter**: each method
authenticates the caller's `userId`, calls a service method, and broadcasts the result. No business logic
lives in a hub.

```
PokerHub   RetroHub   CoffeeHub   StandupHub
     ↘        ↘          ↙            ↙
     RoomService  (join/leave/role/organiser/password/close/delete)
     ↙        ↙          ↘            ↘
PokerService  RetroService  CoffeeService  StandupService
```

- **Shared `RoomSnapshot` fragment.** Each tool broadcasts its own full snapshot after every
  mutation — `PokerSessionUpdated` / `RetroBoardUpdated` / `BoardUpdated` — and all embed the same `RoomSnapshot`
  (short code, name, tool, participants with presence/roles/organiser flags, closed state). Room-level
  fields are defined once and never duplicated per tool.
- **Shared events.** Ephemeral `ReactionReceived` (never persisted) and terminal `RoomClosed` are
  common to every tool.
- **Client → server (poker):** create/join/leave; cast vote; reveal / reset-one / reset-all; set
  auto-reveal, story, story note, deck, password, reactions-enabled, allow-role-change; change role;
  promote/demote/transfer organiser; round-timer start/pause/resume/stop/set-duration; start/end
  discussion; close/delete room; emoji `React`; issue-tracker connect/disconnect/link/submit-points/queue.
- **Client → server (retro):** create (with template/custom columns, anonymity, password, and an
  optional previous board to carry from)/join/leave; add/edit/delete/move card; set template; set
  anonymous; advance/previous/set phase, set phase duration; group/ungroup/rename group, set
  allow-participant-grouping; cast/withdraw dot vote, set vote budget; add/edit/delete action, toggle
  done; set password, reactions-enabled, allow-role-change; change role; promote/demote/transfer
  organiser; close/delete room; emoji `React`.
- **Client → server (standup):** create (with its own questions, password, and an optional previous
  standup to carry from)/join/leave; `Answer` (an empty string clears it); add/assign/toggle-resolved/
  delete blocker; set password, reactions-enabled; promote/transfer organiser; close/delete room;
  emoji `React`. **No phase methods and no timer methods** — see §"The fourth tool, and what it
  refused".
- **Per-recipient projection, pushed per connection.** Every tool hides state that must not leak:
  poker hides vote values before reveal; retro hides other participants' card text during Collect
  (sending only a per-column count), authorship on an anonymous board, and dot totals during Vote;
  coffee hides others' topics while proposing, totals while voting, and the extension split until it
  resolves; standup hides everyone else's answers until you have posted your own. This is done in the
  snapshot projection, never in the client.

  > **Design correction (found while implementing #22/#23).** A retro update cannot be a group
  > broadcast of one payload the way a poker update mostly can: during Collect *what each recipient
  > may see differs*, so `RetroHub` projects and sends the snapshot **per connection**. That is the
  > single enforcement point for all three of the retro's hidden-state rules, which is why they are
  > implemented in one method (`RetroService.ToSnapshot(room, forUserId)`) rather than three places.
- **Reconnection:** the client stores `userId` in `localStorage`; on reconnect the server re-attaches the
  existing participant by `userId` (not connection id), reclaiming their seat, vote/dots, role and
  organiser status.
- **One room, one tool, enforced on join (#32).** A short code names a room, and a room hosts exactly
  one tool, so `RoomService.JoinAsync` takes the calling service's tool and refuses a mismatch with
  `JoinStatus.WrongTool` — **before writing anything**. The rule lives in the room engine rather than
  in either tool, so a third tool inherits it. Before #32 the poker service would seat the
  participant and only then throw projecting a poker snapshot for a retro room, leaving the joiner in
  a room they had been told they did not join.

### The REST surface beside the hubs

Everything interactive goes over a hub; REST carries the things that are not room state — a landing
read, server config, health, the OAuth callback — and file downloads.

```
GET  /api/sessions/{code}                 tool-agnostic landing read for /join/<code>
POST /api/sessions/{code}/analytics       poker: velocity/throughput summary (#11)
POST /api/sessions/{code}/export          poker: completed-round history, csv|json (#12)
POST /api/retro/{code}/export             retro: the whole board, md|csv|json (#28)
GET  /api/integrations/options            which trackers are enabled, and how to connect (#16)
GET  /api/integrations/{provider}/connect  OAuth start; {provider}/callback completes it
GET  /api/config                          retention windows, integration availability
GET  /health, /health/live                readiness (incl. DB) and liveness
```

**Every room-history read is a POST, and deliberately not a GET.** A protected room's contents need
its password, and a password in a query string ends up in server logs, proxy logs and browser history;
the body keeps it out of all three. The cost is that a download must be triggered from script rather
than a plain `<a download>` link — the SPA fetches it and hands it to the browser as an object URL,
in `core/export-transport.ts`, shared by both tools.

**The password guard is `RoomService.VerifyPassword`**, on the room engine, because the password is a
*room* property: the join gate (#2), the retro export (#28) and the poker export (#30) have to reach
the same verdict, and a tool service holding its own hasher is how two of them would eventually stop
agreeing. A room with no password is open, so an absent password is correct for one.

The retro export carries a second guard the poker export has no equivalent of: **the phase**. Before
the discussion starts, an export would hand out cards the team has not seen (#23) and dot totals it
has not reached (#25), so export opens at `Discuss`. The board does not offer the button before then,
making the refusal a backstop rather than the normal path.

> **Design correction (#28, closed by #30): #12 shipped with no password guard at all.**
> This document and the plan both assumed the retro export could reuse "#12's existence-plus-password
> guard" verbatim. There was no password guard in #12 — `GET /api/sessions/{code}/export` checked only
> that the session existed, so a short code alone downloaded a protected session's whole round
> history, and `GET .../analytics` returned a superset of it on the same terms. #28 declined to copy
> that shape and recorded the gap here; #30 closed it, moving both poker routes to guarded POSTs.
> Left open on purpose: the `/join` landing read. It is how the join page learns a password is needed
> at all, and it carries nothing but the room name and that fact.

The retro export's JSON is **camelCase with named enums**, unlike #12's, because it is not only a file:
the read-only summary page parses it directly. One `RetroExportRenderer.ToJson` is shared by the
controller and the tests so there is a single answer to what the payload looks like. #12's export keeps
its own PascalCase options — nothing parses that payload, and changing a shipped file format to match
a convention nobody reads would be churn.


### The third tool, and what it cost (#35)

Lean Coffee was the test of whether the room engine earned its keep. What it needed that already
existed, and reused **unchanged**:

| Primitive | Where it came from |
| --- | --- |
| `PhaseRail<TPhase>` | the retro's rail, generalised here — this was its second consumer (#34) |
| `DotBudget` | the retro's dot voting, generalised: budget enforced from stored rows |
| `Countdown` | the deadline primitive both other tools use (#34) |
| `ActionItemRules` | title validation and owner resolution, shared with retro action items |
| `RoomSweepService` | the 1s sweep loop — the first tool that did not have to write one (#34) |
| `RoomService` | join, roles, organisers, password, close/delete, presence (#19) |
| Rate limiting, a11y, i18n, retention | room-level by construction (#3/#4/#5/#15) |

What was actually **new**: a per-topic timebox rather than a per-phase one, and the extension vote.
Everything else was assembly. That is the answer to the question #34 posed.

> **The tools' countdowns differ deliberately, and the difference is the product.** Poker
> force-reveals on expiry, because a reveal is mechanical. A retro does nothing at all — it clears
> the countdown and leaves the phase to the facilitator (#23). Lean Coffee sits between them: expiry
> opens the keep-going vote and stops. Async Standup has no countdown at all (#36). Four behaviours
> over one `Countdown` primitive, which is why #34 extracted the mechanism and left the actions
> alone.

> **Shared logic, per-tool tables.** `RetroVote` and `CoffeeVote` both implement `IDotVote` and are
> counted by the same code, but they live in their own tables and neither references the other — the
> platform's one structural rule (§"The tools never reference each other"). A shared room-level
> artefact table would be tidier; it would also be a hand-written data migration over shipped retro
> rows, which is why it was not done speculatively.
>
> **The fourth tool arrived and the answer held (#36).** `StandupBlocker` is a third table on the same
> pattern rather than the shared one, because a standup has no dot voting at all — the shared rules
> (`ActionItemRules`) were what it needed, and those were already extracted. The table stays the
> obvious consolidation if a tool ever needs to read another's artefacts, which none does.

> **`core/tool-registry.ts` replaced two growing if/else chains.** The `/join` landing had one for
> picking a hub client and another for picking a route (#32); a third tool would have meant editing
> both. The registry maps `RoomTool` to a route and a lazily-imported client, so a fourth tool is one
> entry — and the client stays out of the initial bundle (#31). #36 was that one entry: no page outside
> `pages/standup/` learned the tool existed.

### The fourth tool, and what it refused (#36)

Where Lean Coffee tested whether the room engine earned its keep, Async Standup tested whether the
platform could say **no**. It is the thinnest tool service of the four, and most of that is deliberate
absence:

| Not used | Why not |
| --- | --- |
| `PhaseRail<TPhase>` | A standup opens, people post, it closes. There is nothing for a facilitator to move the room through. This is the tool that proves the rail is a retro/coffee concern, not a platform one. |
| `Countdown` / `RoomSweepService` | No timebox, so no expiry, so no sweep — and nothing that has to find its boards without a short code. |
| `DotBudget` | Nothing to rank. |
| A tool-specific store port | The other three each needed one (`IPokerRoundStore`, `IRetroBoardStore`, `ICoffeeBoardStore`) to serve a background sweep. `StandupService` needs only `IRoomStore`. |

What it *did* reuse: `RoomService` for everything room-level, `ActionItemRules` for blocker owners
(its third consumer), the per-recipient projection for its one rule, and #27's carry-over shape.
Reaching for the rail just because #34 and #35 had extracted one would have been the wrong kind of
reuse — the point of a shared primitive is that a tool can decline it.

> **Post-to-read is the fourth thing the per-recipient projection enforces**, after retro anonymity
> (#22), retro hidden collection (#23) and the coffee's hidden proposal and extension split (#35).
> Same reason each time: a client-side hide ships everyone's standup to every browser and hopes
> nobody looks. `StandupService.ToSnapshot` is the single place it lives, so every read and every
> broadcast passes through it — including the export, which is why that one renders from the snapshot
> the client already holds rather than from a new server route. Neither of the other two exports could
> do that; both read data their client was never sent.

> **The count, not the names.** The board says "N of the M people in this room have posted" and never
> names who has not. Presence knows who *opened* the room, which is not the same as who is on the
> team: "Dave hasn't posted" would as often mean "Dave is on holiday". A real roster needs accounts,
> and the platform has none — so the honest projection is a count.

> **Retention was decided rather than discovered.** A standup room is idle by construction between
> mornings, so #15's idle eviction applies to it more aggressively than to any other tool. The answer
> is "export it or lose it", stated on the board — the platform's rule, not a per-tool carve-out. The
> alternative, a retention window for one tool, would have been the first place the platform bent to a
> tool rather than the other way round.

> **The constraints this tool wants lifted are platform decisions.** Recurrence needs a scheduler,
> notifications need addresses, a roster needs accounts — and each would change all four tools. So
> each day is its own room, and the invite link is the reminder. If any of that becomes a real problem
> in use, it should be taken deliberately and once, not worked around here.

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
    IRoomStore.cs             # room persistence abstraction (no EF types leak through it);
                              #   a tool with a countdown sweep adds a one-method port: IPokerRoundStore,
                              #   IRetroBoardStore, ICoffeeBoardStore. Standup needs none.
    IClock.cs                 # time abstraction for deterministic time-based tests
    Security/                 # PasswordHasher (PBKDF2)
    Integrations/             # provider-agnostic issue-tracker ports (IIssueTracker, etc.)
    Poker/                    # Estimation domain: PokerService, PokerRoundRules, DeckCatalog,
                              #   StatsCalculator, PokerTimerService, IntegrationService, OAuthService
    Retro/                    # Retrospective domain: RetroService, RetroTemplateCatalog,
                              #   phase state machine, grouping, dot-vote tallies, action items
    Coffee/                   # Lean Coffee domain: CoffeeService, CoffeePhaseRules, per-topic
                              #   timebox, the extension vote, CoffeeTimerService
    Standup/                  # Async Standup domain: StandupService — post-to-read and blockers.
                              #   No phase rail, no countdown, no sweep (#36)
  TeamTools.Data/             # EF Core adapter: TeamToolsDbContext, EfRoomStore, IDatabaseProvider
  TeamTools.Data.{Sqlite,SqlServer,PostgreSql}/   # one project per engine: driver + migrations
  TeamTools.Integrations/     # Jira/ADO/GitHub/GitLab HTTP adapters, OAuth flow, sanitizer, allowlist
  TeamTools.Api/              # Host, thin hub per tool + MVC controllers + health checks
```

Key choices:
- **Logic in the domain projects, not in the hubs/controllers.** Adapters call a service method and
  broadcast the result. Every rule is under fast unit tests.
- **Services return explicit result objects** (e.g. `JoinResult.Ok(snapshot)` / `NameTaken`) rather than
  calling transport back. Tests assert on returned outcomes.
- **`IRoomStore`** hides EF Core; `Core.Tests` use an in-memory fake, `Data.Tests` verify the real
  `EfRoomStore` against SQLite. **`IClock`** makes time-based behaviour (eviction, retention, timer and
  phase expiry) deterministic.
- **A tool-specific query gets a tool-specific port.** `IPokerRoundStore` and `IRetroBoardStore` each
  carry exactly one method — the countdown sweep their tool's background service needs — and are kept
  off `IRoomStore` so the room engine has no knowledge of rounds or phases. One EF adapter implements
  them all, because they share a `DbContext` and therefore a unit of work. **A tool with no countdown
  gets no port at all** — `StandupService` takes only `IRoomStore` (#36).

  > **Both sweeps run once per second, so both narrow in SQL (#14/#33).** They load only the rooms
  > with a running countdown, and apply the `DateTimeOffset` deadline comparison in memory, because
  > SQLite's EF provider cannot translate `DateTimeOffset` ordering. The retro sweep did not: until
  > #33 it went through `GetAllAsync`, loading every room in the database with its participants,
  > round history and the whole board graph — a nine-way join, once a second, whether or not any
  > countdown existed. Invisible on a small database and unbounded on a large one. The general
  > `GetAllAsync` remains, for the callers that genuinely need every room: idle eviction and the
  > retention purge, both on a one-minute cadence.
- **The tools never reference each other.** `TeamTools.Core.Poker`, `TeamTools.Core.Retro`, `TeamTools.Core.Coffee`
  and `TeamTools.Core.Standup` all build on the room engine; a dependency between the tools is the failure mode this structure
  exists to prevent.

  > **Design correction (found while implementing #19).** This document originally specified
  > `TeamTools.Poker` and `TeamTools.Retro` as *separate projects*, for a compile-time guarantee of
  > that rule. Implementing it showed the guarantee cannot be had that cheaply: room-level changes
  > must re-evaluate a tool's completion gate — making someone an observer can complete a poker
  > round — so the room engine has to reach the tool. Across assemblies that needs an event-port
  > indirection in `Core`, and one EF `DbContext` has to own every tool's entities anyway. The tools
  > are therefore **sibling namespaces inside `TeamTools.Core`** (`Core/Poker/`, `Core/Retro/`),
  > with the coupling made explicit instead: `RoomService` takes an `afterChange` **tool hook** that
  > the tool service supplies (poker passes its auto-reveal gate). The rule is now a convention the
  > review enforces rather than the compiler. Recorded here rather than quietly restated, per #17.
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
  core/      room.client.ts        RoomClientBase: connection lifecycle, presence, the shared
                                   RoomClosed event, reactions — everything true of any tool's hub
             poker.client.ts       SignalrRealtimeClient : RoomClientBase, behind IRealtimeClient
             retro.client.ts       SignalrRetroClient    : RoomClientBase, behind IRetroClient
             connection-status.service.ts  the shell's badge, fed by whichever clients exist (#31)
             export-transport.ts   the shared export POST + object-URL download (#28/#30)
             retro-export.service.ts, session-export.service.ts   per-tool export payloads
             models.ts             wire types + the flat view models, with the mappers between them
             localStorage services (identity, membership, theme, decks, tracker), i18n service
  pages/     home     (platform tool picker)
             poker/   (create) — the estimation table lives in pages/session/, see below
             retro/   (create, board, summary)
             join     (resolves a short code -> { tool, shortCode } and routes accordingly)
```

Routes are `/`, `/join/:shortCode`, `/poker/:shortCode`, `/retro/:shortCode`,
`/retro/:shortCode/summary` plus per-tool create routes. `/session/:shortCode` is kept as a
**permanent redirect** to `/poker/:shortCode` so invite links already sitting in people's calendars
keep working.

**Each tool loads on demand (#31/#32).** The five tool pages are `loadComponent` routes; the picker
and the `/join` landing stay eager, because they are the two cold entry points and making an invite
link wait on a chunk would put the latency in the worst place. The tools sharing nothing but the room
engine is what makes them clean split points — and it is why the shell's connection badge reads a
**registry** each client registers itself with, rather than injecting every tool's client: otherwise
the shell would have to know how many tools there are, and the picker would download a realtime
transport it never uses.

The `/join` landing goes further: it `import()`s the hub client for the room's tool once the landing
read tells it which tool that is (#32), so it holds no compile-time knowledge of either tool beyond
the route it navigates to — and `@microsoft/signalr` stays out of the initial bundle even though the
page itself is eager. `RoomClientBase.joinRoom` is the room-level join contract that makes one page
able to drive either client; `JoinStatus` was already shared by both tools.

SignalR sits behind a per-tool interface (`IRealtimeClient` / `IRetroClient`) so components test
against a fake, with no live socket. Component specs (Angular TestBed + fake client) assert
user-visible behaviour. Zoneless, standalone components with Angular signals; Bootstrap 5 with native
color modes for theming — **its CSS only**: nothing here uses Bootstrap's JavaScript, so the bundle
is not loaded (#31). Every modal, dropdown and collapse is signal-driven markup, which is why the
session page has a hand-written Esc handler rather than relying on Bootstrap's.

> **Design correction (#19–#21): there are no reducers, and the poker table did not move.** This
> document specified "pure reducer functions (event + state → new state), unit-tested directly". The
> server sends a **whole snapshot** after every mutation, so there is no incremental event to reduce:
> each client sets one signal from the incoming snapshot, and the only pure functions at that boundary
> are the `flattenSession` / `flattenBoard` mappers that spread the shared `room` fragment up into a
> flat view model. Tests assert on rendered behaviour instead, which is what the reducers existed to
> make possible. Separately, the estimation table still lives in `pages/session/` rather than
> `pages/poker/table/`: the *route* moved to `/poker/:shortCode` in #20 and the folder name simply
> lagged. Not worth a rename that would touch every import for no behavioural gain.

> **`window.__PP_CONFIG__` keeps its old name deliberately.** The split-deployment API base is read
> from a `config.js` that lives in the *deployed* static files and is edited there, not rebuilt.
> Renaming the global would make any deployment whose `config.js` was not updated in the same breath
> as the bundle fall back silently to same-origin — a broken deploy that looks fine. The name is
> internal; the cost of changing it is not.

## Testing strategy

**Test behaviour, not implementation.** Tests assert observable outcomes (resulting state, returned
values/events, what a user sees) — never internal call sequences or private members. Mocks only at true
boundaries (the store, the clock, the realtime transport).

- **`TeamTools.Core.Tests`** — the room engine: `RoomService` through its public API against the
  in-memory store + fake clock; retention/eviction decisions.
- **`TeamTools.Core.Tests` (poker)** — `PokerService` state machine, `DeckCatalog` /
  `StatsCalculator` (table-driven), round timer and discussion phase; `RoomCoreTests` covers the
  room/tool split itself (one tool per room, the shared room fragment, the tool hook).
- **`TeamTools.Core.Tests` (retro)** — `RetroService`: phase transitions and phase-gated mutations,
  grouping, server-side dot budgets, action items, carry-over. **Snapshot-projection tests are
  first-class here:** assert that an anonymous board's snapshot carries no other-author identity
  *on the wire*, that Collect hides others' card text, and that dot totals are absent during Vote —
  and that the **export** carries no author data in any of its three formats.
- **`TeamTools.Data.Tests`** — `EfRoomStore` behaviour against real SQLite (unique constraints, cascade
  delete, query filter, projected queries) **plus a pre-refactor-database upgrade test** for the #19
  migration.
- **`TeamTools.Integrations.Tests`** — tracker adapters against a stubbed `HttpMessageHandler`.
- **`TeamTools.Api.Tests`** — hub/controller/health behaviour via `WebApplicationFactory` + a SignalR
  test client; kept thin (logic is already covered in the domain test projects).
- **`TeamTools.Api.Tests` (exports)** — both exports end to end: a room run over its hub, then read
  and downloaded, asserting the status mapping (404 / 403 / 409) and that an anonymous board names
  nobody in md, csv **or** json. The anonymity guarantee and the password guard are both checked at
  the edge of the system as well as in the domain, because that is where a leak would actually reach
  someone.
- **Frontend** — formatting and mapping unit-tested directly; component specs via TestBed + fake
  client, including the fake export service.

**Coverage:** the hard gate of **≥90% line and branch on `TeamTools.Core`** (`coverage-gate.ps1`)
now covers the room engine and both tool namespaces, with lighter expectations on adapter/wiring
projects. Generated code (EF migrations, Angular boilerplate) is excluded.
Coverage is a guardrail; every test maps to a behaviour. The gate was also the safety net for the #19
refactor, which landed with the suite green and no behavioural change. It runs in CI as well as
locally (`./run.sh test`).

As of task #35 that is **743 backend tests** (Core 607, Integrations 40, Data 44, Api 52) and **247
frontend specs**, with `TeamTools.Core` at ~95% line / ~91% branch. The production bundle is 651 kB
initial (130 kB transfer) against an 800 kB budget.

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

**Provisioning and CI.** `deploy/` holds Terraform for a small AWS deployment (API on EC2 behind a
systemd unit, SPA in S3 + CloudFront, optional Route53/ACM) plus two shell scripts that ship a new
build to it. `.github/workflows/ci.yml` builds and tests both halves on every push and PR to `main`
and runs the Core coverage gate. The resource-name prefix (`app_name`) follows the rename and now
defaults to `teamtools`; a stack created under the old default must pin the old value, since changing
it would replace the bucket and the instance.

## License

Apache License 2.0 with attribution — see [`LICENSE`](./LICENSE) and [`NOTICE`](./NOTICE).
