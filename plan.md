# TeamTools — Feature Plan

**TeamTools** is a platform hosting **two** team-ceremony tools over one shared room
engine: **Planning Poker** (real-time estimation — shipped) and **Team Retro**
(real-time retrospectives — planned in §21–§28, on the platform groundwork laid by
§17–§20, with §29 rewriting the README last).

Each feature below lists **what** it delivers, **why**, the **touch points** in the
codebase, and a sketched **approach**. Implementation order and dependencies live in
[tasks.md](./tasks.md).

§§1–16 are **shipped**, built while the product was planning-poker-only. The
cross-cutting ones — a11y (§4), i18n (§5), rate limiting (§3), retention (§15),
multi-organiser (§7), large-group mode (§6) — become platform-wide the moment §19
lifts room concerns into a shared core; the retro tool inherits them rather than
re-implementing them, and each retro task below carries the checklist item that proves
it.

## Architecture reference (current — single-tool)

- **Backend** — .NET 10, ASP.NET Core, SignalR hub `PlanningPokerHub`
  (`backend/src/PlanningPoker.Api/Hubs/`), domain logic in
  `PlanningPoker.Core/SessionService.cs`, EF Core persistence in
  `PlanningPoker.Data*`. Models: `Core/Models/Session.cs`, `Participant.cs`.
- **Realtime contract** — server broadcasts a full `SessionUpdated` snapshot
  (`Core/Contracts/Snapshots.cs`) after every mutation; ephemeral `ReactionReceived`;
  `SessionClosed`.
- **Frontend** — Angular 21 standalone components, `pages/session/session.page.ts`,
  realtime via `core/realtime.client.ts`, models in `core/models.ts`.
- **Trackers** — provider-agnostic `IIssueTracker` port
  (`Core/Integrations/IssueTracking.cs`); adapters in `PlanningPoker.Integrations`.
- **Existing infra to reuse** — `Hubs/ReactionRateLimiter.cs` (token-bucket pattern),
  `RoundTimerService.cs` (deadline broadcast), `IClock` (testable time).

## Target architecture (platform)

§17 writes this design down before any code moves; §18–§20 build it. Names are
the post-rename ones (§18).

```
TeamTools.Core            Room core: Room, Participant, presence, organiser set,
                          password, close/soft-delete/retention, reactions, IClock,
                          short codes, name normalisation, rate-limit policy.
TeamTools.Poker           Estimation: PokerRound state machine, decks, StatsCalculator,
                          RoundResult, round timer, discussion phase.
TeamTools.Retro           Retrospectives: RetroBoard, columns, cards, groups, votes,
                          action items, phase state machine.
TeamTools.Data*           EF Core model + a project per engine (Sqlite/SqlServer/PostgreSql).
TeamTools.Integrations    Jira / Azure DevOps / GitHub / GitLab adapters (poker-only today).
TeamTools.Api             Hosts both hubs (PokerHub, RetroHub) + REST controllers.
```

- **Room vs. tool state.** A `Room` owns the short code, participants, presence,
  organiser set, password, `ClosedAt`/`DeletedAt` and reaction settings. A room has
  exactly one tool payload — `PokerRound`(+history) **or** `RetroBoard` — chosen at
  creation and immutable thereafter (`RoomTool` discriminator). Rooms are independent;
  there is no team/workspace entity (see §27 for how carry-over works without one).
- **Realtime contract.** Two hubs, one shape: each broadcasts a full tool-specific
  snapshot after every mutation (`PokerSessionUpdated` / `RetroBoardUpdated`), both
  embedding the same `RoomSnapshot` fragment (participants, presence, organisers,
  closed state). Ephemeral `ReactionReceived` and terminal `RoomClosed` are shared.
- **Frontend.** Home page becomes a tool picker; routes split under `/poker/*` and
  `/retro/*` with a shared shell (`core/room.client.ts` for room-level realtime,
  `core/models.ts` gains a `room` fragment mirrored from `RoomSnapshot`).

---

## 1. Who has voted

**What.** During the Voting state, show an anonymous "voted / not yet" indicator per
participant (flipped-card / checkmark) without revealing values.

**Why.** Lets the facilitator see who they're waiting on; foundational for large-group
mode and the discussion prompt.

**Touch points.** `Participant.HasVoted` already exists. Confirm/expose it in the
participant snapshot (`Core/Contracts/Snapshots.cs`); render badges in
`pages/session/session.page.html`.

**Approach.** Ensure `HasVoted` (never the vote value) is in the snapshot during Voting.
Frontend renders a "voted" state per seat. No new persistence.

## 2. Sound / visual cue on reveal

**What.** A short sound + visual flourish (e.g. card-flip animation, consensus pulse)
when votes are revealed; mute toggle stored in `localStorage`.

**Why.** Reveal is the key moment; an audible/visual cue sharpens the ritual for remote
teams.

**Touch points.** Frontend only — `session.page.ts` reacts to `state` transitioning to
`Revealed`; `theme.service.ts` sibling for a `sound.service.ts`; bundle a small audio
asset in `frontend/public`.

**Approach.** Detect `Voting → Revealed` snapshot transition client-side, play cue.
Honor a per-browser mute preference and `prefers-reduced-motion`.

## 3. Rate limiting / abuse protection

**What.** Throttle anonymous session creation, joins, and high-frequency hub calls;
cap participants per session.

**Why.** Public + anonymous = spam/DoS surface. No protection on `CreateSession` today.

**Touch points.** `Hubs/PlanningPokerHub.cs`, reuse the token-bucket approach from
`Hubs/ReactionRateLimiter.cs`; ASP.NET Core rate limiting middleware in `Program.cs` for
the REST endpoints (`SessionsController`).

**Approach.** Per-connection / per-IP buckets for `CreateSession`/`JoinSession`. Add a
max-participants guard in `SessionService`. Return friendly `SessionActionResult`
failures. Configurable limits via `appsettings.json`.

## 4. Accessibility (a11y) baseline

**What.** Full keyboard voting, ARIA roles/labels on cards and controls, focus
management in modals, screen-reader announcement on reveal, visible focus rings,
`prefers-reduced-motion` support.

**Why.** Estimation is a whole-team activity; the card grid and modals are currently
mouse-oriented.

**Touch points.** Cross-cutting frontend — card grid and modals in
`pages/session/session.page.html`, `home.page.html`, `join.page.html`.

**Approach.** Cards as proper buttons in a roving-tabindex radiogroup; `aria-live`
region announcing results on reveal; trap focus in modals and restore on close;
audit color contrast in both themes. Establish a11y conventions before the new UI
features below add more surface.

## 5. i18n / localization

**What.** Externalize UI strings and support multiple locales (number/plural formatting
included).

**Why.** Foundation: every UI feature added afterwards should register translatable
strings rather than hard-coded text.

**Touch points.** Cross-cutting frontend. Adopt Angular's `@angular/localize` or a
runtime library (e.g. transloco) — runtime switching is friendlier for a SPA.

**Approach.** Introduce the i18n library + an `en` base catalog, migrate existing
templates, add a language switcher persisted in `localStorage`. Backend
tracker/error messages map to client-side keys.

## 6. Spectator count / large-group mode

**What.** Gracefully handle 50+ participants: aggregate "N voted of M" summary,
collapsed/virtualized participant list, spectator (observer) count badge.

**Why.** The per-seat layout doesn't scale visually; broadcasting full snapshots to
large rooms needs care.

**Touch points.** Builds on #1 (Who has voted). Frontend rendering in
`session.page.html`; snapshot already carries roles. Consider snapshot trimming in
`SessionService` for very large rooms.

**Approach.** Summary header (voted/total, observer count). Virtualized list above a
threshold. Verify broadcast payload size; trim if needed.

## 7. Facilitator hand-off / multiple organisers

**What.** Allow more than one organiser and transfer/hand-off of the facilitator role;
graceful succession when the organiser leaves.

**Why.** Single `OrganiserUserId` is a single point of failure; co-facilitation is
common. Organiser-gated controls added later (discussion phase) inherit this model.

**Touch points.** `Session.OrganiserUserId` (single) → set of organiser UserIds; authz
checks in `SessionService` (`IsOrganiser`); new hub methods
`PromoteToOrganiser` / `DemoteOrganiser` / `TransferOrganiser`; EF migration.

**Approach.** Add `OrganiserUserIds` (or an `IsOrganiser` flag on `Participant`).
Replace single-organiser checks with set membership. Auto-promote the
longest-connected participant if all organisers disconnect.

## 8. Discussion / re-vote prompt on disagreement

**What.** When votes are split (outliers detected on reveal), surface a prompt nudging
the high/low voters to explain, with a one-click "re-vote" (reset round).

**Why.** Outliers are already computed (`StatsCalculator`) but nothing drives the
conversation around them.

**Touch points.** `StatsCalculator` (outliers exist in the reveal snapshot); frontend
prompt in `session.page.html`; "re-vote" reuses existing `ResetRound`.

**Approach.** On `Revealed`, if `!consensus`, render a discussion banner highlighting
outlier seats and a "Discuss & re-vote" action. Mostly frontend; feeds into #9.

## 9. Timed discussion phase

**What.** A distinct `Discussion` session state between reveal and re-vote, with its own
countdown, entered manually or auto on disagreement (#8).

**Why.** Structures the conversation; separates "discussing" from "estimating" and from
the round timer.

**Touch points.** `SessionState` enum (`Core/Models/Enums.cs`) gains `Discussion`;
state machine in `SessionService`; reuse `RoundTimerService` deadline pattern; new hub
methods `StartDiscussion` / `EndDiscussion`; organiser-gated (respects #7).

**Approach.** Add the state + transitions (`Revealed → Discussion → Voting`). Reuse the
timer deadline broadcast. Discussion-prompt (#8) can trigger entry.

## 10. Notes / comments per story

**What.** Capture free-text notes/justification for the item being estimated; visible to
the room, persisted with the round.

**Why.** Records *why* a number was chosen; feeds export and analytics.

**Touch points.** New persisted field/entity tied to the current story / round record;
hub method `SetStoryNote`; EF migration; snapshot + frontend editor in
`session.page.html`.

**Approach.** Introduce a per-round note (and lay groundwork for a round-history record
that #11/#12 consume). Organiser or anyone (configurable) can edit. Broadcast in
snapshot.

## 11. Velocity / throughput analytics

**What.** Persist a per-round result (story, agreed estimate, stats, duration) and show
session analytics: items estimated, totals, time-per-story, consensus rate.

**Why.** Turns one-off sessions into measurable data; depends on a durable round-history
record that Export also reuses.

**Touch points.** New `RoundResult` entity + EF migration; record on reveal/round-close
in `SessionService`; an "agreed estimate" capture; analytics REST endpoint on
`SessionsController` or a snapshot section; frontend summary view.

**Approach.** On round completion, persist `{ story, finalEstimate, stats, startedAt,
endedAt }`. Compute aggregates server-side. This round-history record is the shared
foundation for #12.

## 12. Export

**What.** Export session/round results to CSV and JSON (and a post-session summary
view).

**Why.** Teams need results outside the app (sprint records, tickets).

**Touch points.** Consumes the `RoundResult` history from #11 and notes from #10. New
REST endpoint `GET /api/sessions/{shortCode}/export?format=csv|json` on
`SessionsController`; frontend download button.

**Approach.** Serialize round history + notes + final estimates. Stream CSV/JSON with
correct content-type. Guard with session existence (and password if set).

## 13. GitHub Issues support

**What.** Add GitHub Issues as an issue-tracker provider: link an issue, read
title/body, push the agreed estimate (label or project field), load a queue from a
repo/search.

**Why.** Broadens reach beyond Jira/ADO; many teams live in GitHub.

**Touch points.** New `IntegrationProvider.GitHub` (`Core/Integrations/IssueTracking.cs`);
new adapter `PlanningPoker.Integrations/GitHubIssueTracker.cs` implementing
`IIssueTracker`; register in `IssueTrackerFactory`; OAuth/PAT config + host allowlist
(`TrackerHostPolicy`); feature flag in `appsettings.json`.

**Approach.** Implement the four `IIssueTracker` operations against the GitHub REST API.
"Story points" has no native field — write to a label or a Projects v2 field
(configurable). Reuse OAuth flow scaffolding.

## 14. GitLab support

**What.** Add GitLab Issues as a provider (link, read, push estimate via weight/label,
queue from project/group/search).

**Why.** Completes the major-tracker set.

**Touch points.** As #13: `IntegrationProvider.GitLab`, `GitLabIssueTracker.cs`,
factory + host policy + flag. Reuses the multi-provider OAuth/config patterns
established by #13.

**Approach.** Implement `IIssueTracker` against the GitLab API. Map story points to the
native issue **weight** field (clean fit), or a label fallback.

## 15. Session retention policy

**What.** A long-term data-lifecycle policy layered on the existing `ClosedAt`/
`DeletedAt` fields (#26): a **closed** (read-only) session is retained 12 months from
`ClosedAt`, then soft-deleted; a **soft-deleted** session is hard-deleted 30 days from
`DeletedAt`; a session that is **neither** closed nor soft-deleted is soft-deleted 30
days from `LastActivityAt`. Replaces the current 60-minute empty/idle hard-delete in
`SessionMaintenanceService`.

**Why.** #26 introduced close/soft-delete but no automatic expiry, so closed/idle
sessions (and their `RoundResult` export/analytics history) would otherwise accumulate
indefinitely. These windows keep history around long enough to be useful without
unbounded growth.

**Touch points.** `SessionMaintenanceService.PurgeAsync` (Core) — replace the
empty/idle-60-min hard-delete branch; `SessionEvictionService` (Api) scheduler, same
1-minute tick; `ISessionStore`/`EfSessionStore` — new query to find soft-deleted
sessions past the hard-delete threshold (must `IgnoreQueryFilters()`, since `DeletedAt`
sits behind a global query filter, `PlanningPokerDbContext.cs:71`); `IClock` for
testable dates.

**Approach.** Keep the 2-minute disconnect-grace participant eviction as-is. On each
pass, for every non-hard-deleted session: soft-deleted past 30 days → hard delete
(cascades `RoundResult`s); closed (not yet soft-deleted) past 12 months → soft delete;
neither, idle past 30 days → soft delete. Broadcast the existing `SessionClosed` event
on hard delete; broadcast an updated snapshot on a soft-delete transition so any
still-connected client learns the session is now read-only/hidden. No new persisted
fields or migration required. Retention windows configurable in `appsettings.json`.
Surface the same windows in the "Close or delete session" modal
(`session.page.html:719-750`, the `danger` modal) so the organiser knows what each
action leads to: next to **Close**, note it will be auto-deleted N months after
closing; next to **Delete**, note it will be permanently removed N days after
deletion. The frontend reads the configured windows rather than hard-coding them
(mirror onto `core/models.ts` / an app-config endpoint, whichever the existing
`appsettings.json`-driven frontend config pattern uses).

## 16. Integrations status & how-to-connect help

**What.** A visible summary of which issue-tracker integrations (Jira, Azure DevOps,
GitHub, GitLab) are enabled on this deployment, with brief per-provider instructions for
connecting (PAT vs. OAuth, where to generate a token, required scopes/permissions, base
URL format).

**Why.** `GET /api/integrations/options` (`IntegrationsController.GetOptions`) already
tells the client which providers are enabled and whether each has OAuth configured, and
this feeds the connect-form dropdown — but there's no explicit overview a user can check
without opening the tracker modal and guessing at what a placeholder like "Personal
access token" actually requires.

**Touch points.** `session.page.ts` (`loadIntegrationOptions`, `enabledProviders`);
tracker modal in `session.page.html` (`case ('tracker')`); `providerLabel`/
`baseUrlPlaceholder`/`tokenPlaceholder` already give provider-specific copy that new
instructions text would sit alongside.

**Approach.** Expand the existing tracker modal (`case ('tracker')` in
`session.page.html`) with a per-provider instructions block — PAT scope/where to
generate, OAuth vs. token, base URL format — gated to `enabledProviders()` so only
providers actually enabled on this deployment are shown. Content goes through the i18n
catalogs like the rest of the UI.

---

# Platform work — two tools

§17 writes the target architecture down **before any code moves**; §18–§20 build the
platform; §21–§28 build the second tool on it; §29 updates the README last.

## 17. ARCHITECTURE.md — document the platform target first

**What.** Rewrite `ARCHITECTURE.md` to describe the two-tool platform — the Room core,
the two tool modules, the two hubs, the shared snapshot fragment, the one-tool-per-room
rule and the table layout after the split — **before** the rename (§18) or the refactor
(§19) touches a line of code.

**Why.** §19 moves nearly every type in `TeamTools.Core` and rewrites the persistence
schema with hand-written data motion. That is exactly the change where "we'll document
it after" produces a refactor that drifts from any plan, and where a reviewer has
nothing to check the migration against. Writing the target down first turns §19 from an
exploratory refactor into an implementation of an agreed design — and the doc is the
artefact that says whether the result is right. It also front-loads the cheapest chance
to discover the design is wrong: on a page, not in a migration.

**Touch points.** `ARCHITECTURE.md` (whole document). The existing doc describes a
single `SessionService`, a single hub and a single `Sessions` table — all three change.

**Approach.** Write it in the future tense of the target, not the present tense of the
code, and say so at the top: this documents where §18–§28 land, and the code catches up
task by task. Cover the Room/tool-payload boundary and which existing fields go where;
the `Rooms` + `PokerRounds` + retro tables shape and the data-motion requirement; the
`RoomSnapshot` fragment and the two-hub contract; the one-tool-per-room rule and the
absence of a team entity; which cross-cutting concerns (§3, §4, §5, §6, §7, §15) are
room-level and therefore inherited by both tools. Keep the existing doc's structure and
level of detail where it still applies rather than starting from a blank page. §29
revisits it only to correct anything §18–§28 discovered — the design should not need
re-explaining at the end.

## 18. Rename to TeamTools

**What.** Rename the solution, projects, namespaces, hub, database context and
user-facing branding from `PlanningPoker.*`/plnpkr to `TeamTools.*`, with the estimation
tool keeping the name "Planning Poker" as a *tool* inside the platform.

**Why.** Every file added by §19–§28 should land with its final name; renaming after the
retro tool exists doubles the diff. `PlanningPoker.Core` housing retro logic would be
actively misleading.

**Touch points.** `backend/PlanningPoker.sln` + all six `src` projects and the test
projects; every `namespace PlanningPoker.*` / `using` line; `PlanningPokerHub.cs` →
`PokerHub.cs`; `PlanningPokerDbContext` → `TeamToolsDbContext` (**note:** the EF
migration snapshot files name the context class — regenerate or hand-edit all three
providers' `*ModelSnapshot.cs` and `*.Designer.cs`); `DesignTimeDbContextFactory.cs` ×3;
`Dockerfile`, `docker-compose.yml`, `.github/workflows`, `deploy/`, `run.sh`, `run.ps1`
(project paths and the `planningpoker.db` default filename); `NOTICE`; frontend
`package.json` name, `index.html` title, and the app-name keys in the i18n catalogs.
`README.md` is deliberately **not** here — it is §29.

**Approach.** Purely mechanical, no behaviour change — its own commit, so the
behavioural diffs that follow stay reviewable. Keep the default SQLite file migratable:
if the old `planningpoker.db` is present, read it and log a rename hint rather than
silently starting empty. `plnpkr` stays as the short brand for the poker tool; the Ko-Fi
link and licence attribution are unchanged.

## 19. Shared Room core

**What.** Extract the tool-agnostic half of `Session` into a `Room` aggregate —
short code, name, participants, presence, organiser set, password, `ClosedAt`/
`DeletedAt`, reaction settings, `LastActivityAt` — leaving estimation-specific state
(`State`, `DeckType`, `CustomCards`, `CurrentStory`, `CurrentStoryNote`, the timer
fields, `LinkedProvider`/`LinkedIssue`/`TicketQueue`, `RoundResult`s) on a `PokerRound`
payload. Add a `RoomTool` discriminator (`Poker` | `Retro`) fixed at creation.

**Why.** This is the platform bet. Every cross-cutting feature already shipped — rate
limiting (§3), a11y (§4), i18n (§5), large-group mode (§6), multi-organiser and
succession (§7), retention (§15) — is room-level, not poker-level. Lifting them once
means the retro tool inherits them and a third tool is cheap. The cheaper alternative
(nullable retro columns on `Session`) pays for itself once and then charges rent
forever.

**Touch points.** `Core/Models/Session.cs` → `Room.cs` + `Poker/PokerRound.cs`;
`Participant.cs` (`SessionId` → `RoomId`); `ISessionStore` splits into `IRoomStore`
(find-by-short-code, add, update, remove, retention queries, `AreReactionsEnabledAsync`)
plus a poker-specific store for `GetSessionsWithExpiredTimerAsync`; `SessionService.cs`
splits into `RoomService` (join/leave/rename/role/organiser/password/close/delete) and
`PokerService` (vote/reveal/reset/deck/story/timer/discussion);
`SessionMaintenanceService` and `RetentionOptions` move to room level;
`Contracts/Snapshots.cs` grows a `RoomSnapshot` fragment that each tool snapshot embeds;
`Hubs/PokerHub.cs`, `ConnectionRegistry`, `HubThrottle`, `ReactionRateLimiter` become
room-scoped; `EfSessionStore` + `TeamToolsDbContext` + an EF migration for all three
providers; frontend `core/models.ts` mirrors `RoomSnapshot`, and
`core/realtime.client.ts` splits into a shared `room.client.ts` + `poker.client.ts`.

**Approach.** A behaviour-preserving refactor implementing the boundary §17 wrote down,
verified by the existing suite — the ≥90% Core coverage gate is the safety net, so keep
it green and add no features in this commit. Table shape: `Rooms` (renamed from
`Sessions`, minus the poker columns, plus `Tool`) and `PokerRounds` (1:1 with a poker
room, owning the moved columns). The migration must **move** existing data, not drop it,
so write the data-motion SQL by hand rather than accepting a scaffolded
drop-and-recreate. Invite links (`/join/<code>`) keep working: the join page resolves a
short code to `{ tool, shortCode }` and routes to the right tool page. Existing hub
method names and their client-visible payloads stay as they are, apart from the snapshot
gaining its `room` fragment.

## 20. Tool picker & platform shell

**What.** The home page becomes a platform landing page offering both tools; routes
split into `/poker/*` and `/retro/*` under a shared shell (header, theme toggle,
language switcher, tool switcher).

**Why.** Two tools need a front door. Today `home.page` hard-codes deck choice and
"create session" — poker-only concepts sitting on the platform's entry point.

**Touch points.** `app.routes.ts` (`/`, `/join/:shortCode`, `/session/:shortCode` →
`/`, `/join/:shortCode`, `/poker/:shortCode`, `/retro/:shortCode`, plus the per-tool
create routes); `pages/home/home.page.*` splits into a picker plus
`pages/poker/create`; `app.html` shell; `join.page.ts` (resolve the tool from the short
code, per §19); the en/es/pt/pl i18n catalogs gain platform and tool-name keys.

**Approach.** Keep `/session/:shortCode` as a permanent redirect to `/poker/:shortCode`
so links already sitting in people's calendars survive. The picker inherits the §4 a11y
conventions (keyboard-navigable cards, visible focus rings) and all copy goes through
the i18n catalogs — no hard-coded strings.

## 21. Retro board: model, creation, cards

**What.** The retro tool's foundation: a `RetroBoard` with column templates (Went well /
To improve / Action items, Start-Stop-Continue, 4Ls, Mad-Sad-Glad, Custom) and cards
that participants add, edit, delete and move between columns in real time.

**Why.** Everything else in the retro tool — phases, grouping, voting, actions,
export — operates on cards in columns. This is the analogue of the deck plus votes.

**Touch points.** New `TeamTools.Retro`: `RetroBoard.cs` (settings + columns),
`RetroColumn.cs`, `RetroCard.cs`, `RetroTemplateCatalog.cs` (mirrors the existing
`DeckCatalog.cs` pattern — built-in templates plus a Custom escape hatch),
`RetroService.cs` (mirrors `PokerService`); `Contracts/RetroSnapshots.cs` embedding
`RoomSnapshot` (§19); `Hubs/RetroHub.cs` (`AddCard`, `EditCard`, `DeleteCard`,
`MoveCard`, `SetTemplate`); EF migration ×3; frontend `pages/retro/retro.page.*`,
`core/retro.client.ts`, `core/models.ts`.

**Approach.** A card is `{ Id, ColumnId, AuthorUserId, Text, CreatedAt, Order }`,
editable and deletable by its author or an organiser (the §7 organiser set, unchanged).
Broadcast the full board snapshot per mutation exactly as poker does — the §6
large-group work already proved that shape at 50+ participants, and a board is a
comparable payload. Cap card text length and rate-limit adds through the §3 token
bucket. Reuse the §10 note-editing conventions for inline card editing.

## 22. Retro anonymity

**What.** A board-level facilitator setting: cards are **attributed** (author shown) or
**anonymous**. Anonymous means the snapshot carries no author identity for other
participants — not merely a hidden UI field.

**Why.** Psychological safety is the point of a retro, and a client-side-only hide is a
promise one devtools panel disproves. Settling this in the snapshot contract *before*
grouping, voting and export exist is what stops those three leaking authorship later.

**Touch points.** `RetroBoard.Anonymous` (organiser-settable, and — see approach — only
while the board is empty); the `RetroService` snapshot projection;
`RetroSnapshots.cs` (`AuthorUserId` null for everyone but the author, with `IsMine`
computed per recipient); `RetroHub.SetAnonymous`; EF migration ×3; frontend card
rendering.

**Approach.** The snapshot is already projected per recipient in poker (votes hidden
pre-reveal) — the same mechanism applies: strip `AuthorUserId` for everyone but the
author, and never send it at all on an anonymous board, while still marking the
caller's own cards so they can edit them. `AuthorUserId` is still *stored* — an author
must be able to edit their own card and organiser moderation needs a target — so
document plainly that this is anonymity from participants, not from a database
administrator. Lock the toggle once the first card exists: flipping it mid-retro would
retroactively expose cards written under a promise of anonymity.

## 23. Facilitator-driven phases

**What.** A retro phase state machine — **Collect → Group → Vote → Discuss → Actions →
Closed** — advanced by an organiser, with cards hidden from other participants until
Collect ends, and an optional countdown per phase.

**Why.** An unstructured board is a free-for-all where the first loud voice anchors
everyone. Hidden collection is the retro equivalent of hidden voting — and it is the
reason the per-recipient snapshot projection matters.

**Touch points.** A `RetroPhase` enum plus transitions in `RetroService`;
`RetroHub.AdvancePhase` / `SetPhase` (organiser-gated via §7); `RoundTimerService.cs`
generalised into a room-level phase timer reusing its existing deadline-broadcast
pattern (§9 established it for the discussion phase); snapshot plus a frontend phase
rail; EF migration ×3 (persisted enum + deadline, mirroring the poker `State` and
`TimerDeadline` columns).

**Approach.** Model transitions explicitly and forward-only, with an organiser-only
"back one phase" escape hatch — facilitators mis-click. During Collect a participant
sees their own cards plus a count of everyone else's: the same affordance as §1. Gate
mutations by phase (no new cards in Vote, no votes in Collect) and return friendly
`SessionActionResult` failures. Announce phase changes through the §4 `aria-live`
region.

## 24. Grouping into themes

**What.** In the Group phase, drag cards onto one another to form named theme groups;
groups collapse and expand, can be renamed and ungrouped, and become the unit that
voting and discussion operate on.

**Why.** Twelve cards saying the same thing should be one conversation and one vote
target — otherwise dot voting splits across duplicates and the real top theme loses.

**Touch points.** `RetroGroup.cs` (`{ Id, BoardId, Label, Order }`) and
`RetroCard.GroupId`; `RetroHub.GroupCards` / `UngroupCard` / `RenameGroup`; snapshot;
EF migration ×3; frontend drag-and-drop in `retro.page.*`.

**Approach.** Grouping is organiser-driven by default, with a board setting to open it
to everyone. Concurrent drags resolve last-write-wins on `GroupId`, with the
full-snapshot rebroadcast reconciling divergence — cheap and correct at room scale.
Drag-and-drop needs a keyboard and screen-reader equivalent to satisfy §4: a "move card
to group" menu on every card, not a mouse-only affordance.

## 25. Dot voting

**What.** In the Vote phase each participant spends a configurable budget of dots across
cards and groups (multiple dots on one item allowed or not, per board setting); the
result orders the Discuss phase.

**Why.** It turns a wall of cards into a ranked agenda in about ninety seconds, and it
gives the quiet half of the team equal weight.

**Touch points.** `RetroVote.cs` (`{ BoardId, VoterUserId, TargetKind, TargetId }`) and
`RetroBoard.VoteBudget` / `AllowMultiplePerItem`; `RetroHub.CastRetroVote` /
`WithdrawVote`; a `StatsCalculator` sibling for tallies; snapshot (own dots always
visible, others' totals only once the Vote phase ends); EF migration ×3; frontend dot
controls plus the ranked list.

**Approach.** Enforce the budget server-side — never trust a client dot count. Totals
stay hidden during Vote (the same anchoring argument as §23) and reveal on the
transition to Discuss, where the board reorders by score. Votes follow their target
through grouping: grouping cards sums their dots, ungrouping returns each card's own.
Voting is per participant, so anonymity (§22) is unaffected.

## 26. Action items

**What.** First-class action items with a title, an optional owner (a participant or a
free-text name) and an optional due date, created in the Actions phase — typically from
a discussed theme — persisted with the board and markable done.

**Why.** A retro whose outcomes evaporate is theatre. Actions are also the only retro
artefact with a life *after* the meeting, which is what makes §27 and §28 worth having.

**Touch points.** `RetroActionItem.cs` (`{ Id, BoardId, Title, OwnerUserId, OwnerName,
DueDate, DoneAt, SourceGroupId?, CarriedFromBoardId? }`); `RetroHub.AddAction` /
`EditAction` / `ToggleActionDone` / `DeleteAction`; snapshot; EF migration ×3; frontend
actions panel; locale-aware date rendering through the §5 i18n service (`Intl`).

**Approach.** Creatable from a theme (title prefilled from the group label) or
standalone. The owner is optional and free-text-capable: the platform has no accounts,
and the owner may be someone who was not in the room. Actions stay editable after the
board is closed, because "mark done" happens days later — that is deliberately the one
write allowed on a closed room, and it needs an explicit carve-out in the §19 close
check rather than a silent exception.

## 27. Carry-over from the previous retro

**What.** Starting a retro can pull the unfinished action items from a previous board
forward, shown as a review list at the top of the Collect phase.

**Why.** "What happened to last time's actions?" is the highest-value two minutes of a
retro. Rooms are independent — there is no team entity — so carry-over needs an
explicit link.

**Touch points.** The retro creation flow (`pages/retro/create`) accepts a previous
board's short code; `RetroService.CreateWithCarryOverAsync`;
`RetroActionItem.CarriedFromBoardId` (§26); `RetroBoard.PreviousBoardShortCode`; a
snapshot section; EF migration ×3.

**Approach.** **Copy** the not-done actions into the new board rather than referencing
them, keeping `CarriedFromBoardId` for provenance — the new board then stays
self-contained for export (§28) and is unaffected when the old one is
retention-deleted (§15). Require the previous board's **password** if it had one: a
short code is a bearer token here, and carry-over must not become a way to read a
protected board's contents. Deep-link it too, so "start the next retro" from a closing
board prefills the code.

## 28. Retro export

**What.** Export a retro board — columns, cards, groups, vote tallies, action items — to
CSV, JSON and Markdown, plus a read-only post-retro summary view.

**Why.** Retro output belongs in the team's wiki or ticket tracker, and pasteable
Markdown is the format that actually gets used. This extends the §12 poker export
rather than inventing a second mechanism.

**Touch points.** `GET /api/retro/{shortCode}/export?format=csv|json|md` alongside the
existing `GET /api/sessions/{shortCode}/export` (§12) — both move under the shared room
export controller from §19; frontend download button plus the summary view.

**Approach.** Reuse §12's streaming and content-type handling and its
existence-plus-password guard verbatim. **Anonymity (§22) must hold in the export:** an
anonymous board exports no author column at all, and there is no organiser override
that de-anonymises it — an export that quietly attributes anonymous cards would be the
worst possible bug in this feature. Markdown output is grouped by theme, ordered by
dots, with actions as a task list.

## 29. README & deploy — last

**What.** Rewrite `README.md` around the two-tool platform (intro, per-tool feature
lists, layout block, run instructions), reconcile `ARCHITECTURE.md` with anything
§18–§28 discovered, and finish any deploy/CI wiring the rename (§18) could not settle
in advance.

**Why.** The README is the product's front page and it describes features as *shipped*.
Rewriting it before the retro tool exists would advertise something a visitor cannot
use — which is why it is deliberately excluded from the §18 rename and lands here
instead. `ARCHITECTURE.md` is the opposite case: it describes the *design*, so it goes
first (§17) and only needs correcting at the end.

**Touch points.** `README.md` (platform intro, per-tool feature lists, layout block,
run/prerequisite instructions); `ARCHITECTURE.md` (corrections only — the design was
written in §17); `.github/workflows` and `deploy/` if the retro tool added anything;
`run.sh` / `run.ps1` help text.

**Approach.** One commit, once the shape has stopped moving, so the docs match a single
point in the history. Per the repo convention, docs-only edits do not require running
the test suites to source a number. If §17's design turned out to be wrong somewhere,
say so in the doc's history rather than quietly rewriting it to match the code — the
divergence is worth knowing about.

## 30. Password-guard the poker round history — follow-up to §12/§28

**What.** Put the poker round-history reads behind the room password, as §28 already put
the retro export: `POST /api/sessions/{shortCode}/export` and
`POST /api/sessions/{shortCode}/analytics`, both refusing without the password.

**Why.** §12 shipped with no password guard at all. `GET .../export` checked only that
the session existed, so a short code alone downloaded a protected session's whole round
history — every story, note and estimate — and `GET .../analytics` returned a superset of
it on the same terms. The password gated *joining* the room but not reading it back out,
which makes it a door with no wall. Found while implementing §28, which declined to copy
the shape and recorded the gap in `ARCHITECTURE.md` instead of quietly inheriting it.

**Touch points.** `PokerService.GetAnalyticsAsync` / `GetAnalyticsCsvAsync` (now
password-taking and status-returning); a new `SessionExportStatus`; `RoomService`
(a shared `VerifyPassword`, so the join gate and both tools' exports cannot drift);
`SessionsController`; the frontend analytics modal and export buttons; `AnalyticsTests`,
`ExportEndpointTests`, `session.page.spec.ts`.

**Approach.** Follow §28's shape rather than inventing a second one — same 403, same
"ask only after the server refuses" prompt, same POST-and-object-URL download, with the
transport extracted so both tools share it. **Analytics is guarded too**, not just the
file download: it returns a strict superset of the CSV, so guarding one and not the other
would be theatre. The `/join` landing read stays **open** on purpose — it is how the join
page learns a password is needed at all, and it carries nothing but the room name and that
fact. Moving both routes from GET to POST is a breaking API change in principle, but the
SPA ships from the same artifact and changes in lockstep, and a GET that could carry a
password in its query string is exactly what §28 refused.

## 31. Get the frontend bundle back under its budget — follow-up to §29

**What.** Bring the initial bundle back under the 800 kB budget it had been quietly
exceeding, by loading each tool on demand and dropping what the app never used.

**Why.** `ng build` had been printing "bundle initial exceeded maximum budget" for
several tasks — 889 kB before §28's export UI, 910 kB after. CI runs the production
build but does not fail on a budget warning, so nobody was stopped by it. Everything was
eagerly imported: a visitor to a poker table downloaded the retro board's grouping and
dot-voting code, and a visitor to the picker downloaded both tools plus the realtime
transport. The two tools deliberately share nothing but the room engine, which makes them
the natural split.

**Touch points.** `app.routes.ts` (`loadComponent` per tool page); a new
`core/connection-status.service.ts` so the shell's connection badge no longer injects both
tool clients; `angular.json` (`scripts`).

**Approach.** Three changes, measured one at a time so each earns its place. (1) Lazy
routes for the five tool pages, with the picker and the `/join` landing kept **eager** —
they are the two cold entry points, and making an invite link wait on a chunk would put
the latency in the worst place. (2) The shell's badge reads a registry that each tool
client registers itself with, rather than injecting both clients: the shell stops knowing
how many tools exist, and stops pulling their transport into the initial graph. (3) Drop
`bootstrap.bundle.min.js` — nothing in the app uses Bootstrap's JavaScript; every modal,
dropdown and collapse here is signal-driven markup, which is why the Esc handler is
hand-written. **Do not raise the budget** to make the warning go away; if the number still
does not fit after the split, argue for the new number.

## 32. Join a room over its own tool's hub — follow-up to §19/§31

**What.** Make `/join/<code>` join over the hub belonging to the room's tool, and stop
either tool service from seating a participant in a room it does not host.

**Why.** The join page sent *every* join to the **poker** hub. For a retro room the room
engine seated the participant and the poker service then threw projecting a snapshot for
a room with no round — so the joiner saw "could not reach the server" while their seat had
in fact been taken. Retro invite links did not work at all, and the failure blamed the
server. §31 spotted it while looking at the bundle and left it as a behavioural question.

**Touch points.** `RoomService.JoinAsync` (an expected-tool argument) and a new
`JoinStatus.WrongTool`; `PokerService`/`RetroService` pass their own tool;
`RoomClientBase` gains the room-level `joinRoom` contract both clients implement;
`join.page.ts` resolves the client from the landing read's tool.

**Approach.** Fix both halves. The **client** picks the right hub — it already reads the
tool from the landing response to know where to navigate, so it has what it needs; the
client is imported on demand, once the tool is known, which also gets the realtime
transport out of the initial bundle that §31 could not move. The **server** refuses a
cross-tool join *before* writing anything, in the room engine rather than in either tool,
so the guarantee is one rule a third tool would inherit and not two rules that can drift.
A refused join must leave no seat behind — that half-served state was the damaging part.
The `/join` landing read stays tool-agnostic: it is what tells the client which tool it is
dealing with.

## 33. Narrow the retro phase-countdown sweep — follow-up to §23

**What.** Give the retro phase-countdown expiry pass its own narrowed store query, as the
poker round timer already has, instead of loading every room in the database once a
second.

**Why.** `RetroPhaseTimerService` asked `IRoomStore.GetAllAsync()` which room was due —
and `GetAllAsync` is `WithPayload(_db.Rooms)`: a nine-way `LEFT JOIN` pulling every room
together with its participants, round history and the whole retro board graph (columns,
cards, groups, votes, action items), with the `PhaseDeadline` comparison then applied in
memory. That ran **once per second, forever**, whether or not any countdown existed. It
also emitted EF's `MultipleCollectionIncludeWarning` — five collection includes fanned out
into one near-cartesian result set. On an empty dev database it costs nothing visible,
which is why it went unnoticed; the cost grows with every room ever created, not with the
rooms that actually have a timer running.

**Touch points.** A new `IRetroBoardStore` (the retro sibling of `IPokerRoundStore`);
`EfRoomStore` implements it; `RetroPhaseTimerService` takes it; `Program.cs` registers it;
`FakeRoomStore` mirrors the query as the poker one is mirrored.

**Approach.** Copy the shape the poker sweep already established (§14) rather than invent
a second one: narrow in SQL, keep the `DateTimeOffset` comparison in memory because
SQLite's EF provider cannot translate `DateTimeOffset` ordering, and keep the port off
`IRoomStore` so the room engine carries no knowledge of countdowns. Include the **board
only** — no collections: the sweep clears the deadline and saves, and the background
service re-reads each board per recipient anyway, because a retro snapshot is projected
per viewer (§21). Tests must assert what is *not* loaded, or the includes creep back.

**Not in scope.** Both sweeps still tick every second when nothing is running. Making
them event-driven (schedule against the next known deadline) would remove the idle
queries entirely, but it is a scheduling change with its own failure modes — and at one
cheap indexed query per second it is not yet worth them.

---

## Cross-cutting notes

- **Tests.** Backend has a ≥90% Core coverage gate (xUnit); every Core change needs unit
  tests against the in-memory store + fake `IClock`. Frontend uses Vitest + TestBed.
- **Migrations.** Any `Room`/`Participant`/new-entity change needs EF migrations for
  all three providers (Sqlite, SqlServer, PostgreSql).
- **Snapshot contract.** New broadcast fields go through the tool's snapshot record and
  the mirrored `core/models.ts` — keep them in sync. Room-level fields belong in the
  shared `RoomSnapshot` fragment (§19), never duplicated per tool.
- **Scaling caveat.** The app is single-instance (in-process SignalR + SQLite). Analytics
  history and large-group broadcasts are fine at that scale; horizontal scaling (Redis
  backplane) is out of scope here.
- **One tool per room.** A room is created as poker *or* retro and cannot switch. There
  is no team/workspace entity: the two tools share the room engine and the front door,
  nothing else (§27 is the one deliberate cross-board link, and it copies rather than
  references).
- **Cross-cutting parity.** Anything room-level added from §19 onward must work for
  both tools by construction. Each retro task carries an explicit a11y + i18n checklist
  item precisely so the new surface does not quietly regress §4 and §5.
- **Docs cadence.** `ARCHITECTURE.md` is written **first** (§17), before the rename or
  the refactor, because it is the design the refactor implements. `README.md` is written
  **last** (§29), because it describes shipped features to visitors. The two are not one
  docs task — they answer to opposite deadlines.
