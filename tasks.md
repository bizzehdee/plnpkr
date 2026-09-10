# TeamTools — Task Queue

Tasks derived from [plan.md](./plan.md), ordered for execution. Ordering rule:
**ascending ease of implementation, but a task that others depend on always comes
before its dependents** (dependency wins over pure ease). Effort is a rough T-shirt size.

Two phases: **#1–#16 (done)** built the planning-poker tool, and **#17–#29** turn the app
into the TeamTools platform and add the second tool, Team Retro.

## Phase 1 — Planning Poker (#1–#16, all done)

| # | Task | Effort | Depends on | Layer |
|---|------|--------|-----------|-------|
| 1 | Who has voted | S | — | FE (+snapshot) |
| 2 | Sound / visual cue on reveal | S | — | FE |
| 3 | Rate limiting / abuse protection | S–M | — | BE |
| 4 | Accessibility baseline | M | 1 | FE |
| 5 | i18n / localization | M | — | FE |
| 6 | Spectator count / large-group mode | M | 1 | FE |
| 7 | Facilitator hand-off / multiple organisers | M | — | BE + FE |
| 8 | Discussion / re-vote prompt on disagreement | M | 1 | FE |
| 9 | Timed discussion phase | M–L | 7, 8 | BE + FE |
| 10 | Notes / comments per story | M | — | BE + FE |
| 11 | Velocity / throughput analytics | L | 10 | BE + FE |
| 12 | Export | M | 10, 11 | BE + FE |
| 13 | GitHub Issues support | L | — | BE |
| 14 | GitLab support | L | 13 | BE |
| 15 | Session retention policy | S–M | — | BE + FE |
| 16 | Integrations status & how-to-connect help | S | 5, 13, 14 | FE |

> **Why this order.** #1, #2, #3 are small and unblock nothing-but-themselves, so they go
> first. #4 (a11y) and #5 (i18n) are cross-cutting foundations — done before the larger
> UI features so new surface inherits the conventions. #7 (multi-organiser) precedes #9
> because the timed discussion phase adds organiser-gated controls. The history-backed
> trio is a dependency chain: notes (#10) → analytics/round-history (#11) → export (#12).
> Trackers are independent and last (largest); #13 establishes the multi-provider patterns
> #14 reuses.

---

## 1. Who has voted  `S`
**Foundation for #4, #6, #8.**
- [x] Confirm `Participant.HasVoted` is in the participant snapshot (`Core/Contracts/Snapshots.cs`) during Voting — value never included.
- [x] Render per-seat "voted / waiting" badge in `pages/session/session.page.html`.
- [x] Mirror any snapshot field in `core/models.ts`.
- [x] Tests: snapshot omits vote value pre-reveal; FE renders badge.

## 2. Sound / visual cue on reveal  `S`
- [x] Add `core/sound.service.ts` + a small audio asset in `frontend/public`. *(shipped as `core/reveal-cue.service.ts`, CSS-driven flourish — no audio asset)*
- [x] Detect `Voting → Revealed` transition in `session.page.ts`; play cue + reveal animation.
- [x] Per-browser mute toggle in `localStorage`; respect `prefers-reduced-motion`.
- [x] Tests: transition triggers cue; mute suppresses it.

## 3. Rate limiting / abuse protection  `S–M`
- [x] Token-bucket per connection/IP for `CreateSession`/`JoinSession` in `Hubs/PlanningPokerHub.cs` (reuse `Hubs/ReactionRateLimiter.cs` pattern).
- [x] Max-participants guard in `SessionService`; friendly `SessionActionResult` failures.
- [x] ASP.NET Core rate-limiting middleware for REST endpoints in `Program.cs`.
- [x] Limits configurable in `appsettings.json`.
- [x] Tests: exceeding the bucket is rejected; under-limit passes.

## 4. Accessibility baseline  `M`  — depends on #1
- [x] Cards → buttons in a roving-tabindex radiogroup; full keyboard voting.
- [x] `aria-live` region announcing results/vote-status on reveal (uses #1).
- [x] Focus trap + restore in all modals; visible focus rings.
- [x] Color-contrast audit in light + dark themes.
- [x] Tests: keyboard cast vote; aria attributes present.

## 5. i18n / localization  `M`
**Do before later UI features so their strings are translatable.**
- [x] Choose + wire i18n library (runtime-switchable preferred); add `en` base catalog.
- [x] Migrate existing `home`/`join`/`session` templates to translation keys, incl. deck labels (`DECK_LABEL_KEYS`) shared by home + session.
- [x] Language switcher persisted in `localStorage`.
- [x] Locale-aware number/plural formatting. `I18nService.formatNumber` (`Intl.NumberFormat`) + `LocaleNumberPipe` (`ln`) replace the fixed-`LOCALE_ID` `DecimalPipe`; `I18nService.plural` (`Intl.PluralRules`) + `PluralPipe` (`plural`) select the right CLDR category per locale (Polish: one/few/many/other), applied to the participant-count string.
- [x] Map backend tracker/error messages to client keys. `err.*` keys cover every fixed-text `JoinStatus`/`IntegrationStatus` (`INTEGRATION_ERROR_KEYS` map for the latter); statuses whose message is inherently dynamic (`AuthFailed`/`IssueNotFound`/`ProviderError`, carrying live text from the external tracker API) intentionally keep showing the server's own message — there's nothing static to translate there.
- [x] Tests: switching locale swaps strings.

## 6. Spectator count / large-group mode  `M`  — depends on #1
- [x] Summary header: "N voted of M", observer/spectator count badge.
- [x] Virtualized/collapsed participant list above a threshold.
- [x] Verify `SessionUpdated` payload size for large rooms; trim snapshot in `SessionService` if needed.
- [x] Tests: summary counts correct; large list virtualizes.

## 7. Facilitator hand-off / multiple organisers  `M`  — needed by #9
- [x] Model: single `Session.OrganiserUserId` → multiple (set, or `Participant.IsOrganiser`). EF migration (all 3 providers).
- [x] Replace single-organiser authz in `SessionService` with set membership.
- [x] Hub methods: `PromoteToOrganiser`, `DemoteOrganiser`, `TransferOrganiser`.
- [x] Auto-succession when all organisers disconnect (longest-connected wins).
- [x] Snapshot + FE controls; mirror in `core/models.ts`.
- [x] Tests: promote/demote/transfer authz; succession.

## 8. Discussion / re-vote prompt on disagreement  `M`  — depends on #1
- [x] On `Revealed` with `!consensus`, render discussion banner highlighting outlier seats (outliers already in reveal snapshot via `StatsCalculator`).
- [x] "Discuss & re-vote" action reusing existing `ResetRound`.
- [x] Tests: banner shows only on disagreement; action resets round.

## 9. Timed discussion phase  `M–L`  — depends on #7, #8
- [x] Add `SessionState.Discussion` (`Core/Models/Enums.cs`) + transitions `Revealed → Discussion → Voting` in `SessionService`.
- [x] Hub methods `StartDiscussion`/`EndDiscussion`, organiser-gated (respects #7).
- [x] Reuse `RoundTimerService` deadline broadcast for the discussion countdown.
- [x] Entry can be auto-triggered by the #8 prompt.
- [x] Snapshot + FE state; mirror in `core/models.ts`. EF migration if state persisted. *(no migration needed — `State` was already a persisted enum column)*
- [x] Tests: state machine transitions; timer expiry behavior.

## 10. Notes / comments per story  `M`  — foundation for #11, #12
- [x] Persisted per-round note field/entity; EF migration (all 3 providers).
- [x] Hub method `SetStoryNote`; configurable who may edit (organiser vs anyone).
- [x] Broadcast in snapshot; FE editor in `session.page.html`; mirror in `core/models.ts`.
- [x] Lay groundwork for a round-history record (#11 extends it).
- [x] Tests: note persists + broadcasts; edit authz.

## 11. Velocity / throughput analytics  `L`  — depends on #10
- [x] New `RoundResult` entity `{ story, finalEstimate, stats, startedAt, endedAt }` + EF migration (all 3 providers).
- [x] Capture "agreed estimate" on round completion; persist round record in `SessionService`.
- [x] Server-side aggregates: items estimated, totals, time-per-story, consensus rate.
- [x] Expose via `SessionsController` endpoint or snapshot section; FE summary view.
- [x] Tests: round recorded on completion; aggregates correct.

## 12. Export  `M`  — depends on #10, #11
- [x] `GET /api/sessions/{shortCode}/export?format=csv|json` on `SessionsController`.
- [x] Serialize round history (#11) + notes (#10) + final estimates; correct content-type/streaming.
- [x] Guard with session existence (+ password if set).
- [x] FE download button + post-session summary view.
- [x] Tests: CSV + JSON shape; auth guard.

## 13. GitHub Issues support  `L`
**Establishes multi-provider patterns reused by #14.**
- [x] `IntegrationProvider.GitHub` in `Core/Integrations/IssueTracking.cs`.
- [x] `PlanningPoker.Integrations/GitHubIssueTracker.cs` implementing `IIssueTracker` (Validate/GetIssue/SetStoryPoints/Search).
- [x] Map story points → label or Projects v2 field (configurable).
- [x] Register in `IssueTrackerFactory`; OAuth/PAT config; host allowlist (`TrackerHostPolicy`); feature flag in `appsettings.json`.
- [x] Tests: adapter ops against a fake HTTP handler; factory resolution.

## 14. GitLab support  `L`  — depends on #13
- [x] `IntegrationProvider.GitLab`; `GitLabIssueTracker.cs` implementing `IIssueTracker`.
- [x] Map story points → native issue **weight** (label fallback).
- [x] Register in factory + host policy + feature flag; reuse #13 OAuth/config patterns.
- [x] Queue from project/group/search.
- [x] Tests: adapter ops; factory resolution.

## 15. Session retention policy  `S–M`  ✅ done
**Builds on the `ClosedAt`/`DeletedAt` fields from #26; no new persistence.**
- [x] `ISessionStore.GetSoftDeletedPastRetentionAsync` (EF: `IgnoreQueryFilters()` past the `DeletedAt` global filter) + `FakeSessionStore` equivalent.
- [x] Replaced the empty/idle-60-min hard-delete branch in `SessionMaintenanceService.PurgeAsync` with: soft-deleted 30d → hard delete; closed 12mo (not yet soft-deleted) → soft delete; neither, idle 30d → soft delete.
- [x] `SessionEvictionService`: broadcasts `SessionClosed` for both hard deletes and newly-soft-deleted sessions (matches the existing organiser-triggered `DeleteSessionAsync` convention — a soft-deleted session is gone from every read, so clients get "closed", not a snapshot).
- [x] Retention windows configurable via `Retention:ClosedRetentionMonths`/`SoftDeleteRetentionDays`/`IdleRetentionDays` (`RetentionOptions`, same `GetValue`-with-default pattern as `SessionLimits`/`HubThrottle`).
- [x] Exposed via new `GET /api/config` (`ConfigController`) — small, growable server-config surface.
- [x] Shown in the "Close or delete session" modal: next to **Close**, "Automatically deleted N months after closing."; next to **Delete**, "Permanently removed N days after deletion." (pluralized correctly per locale via `PluralPipe`).
- [x] Tests: all three transition rules + just-under-threshold boundaries (`SessionMaintenanceTests`); hard delete removes `RoundResult`s + `GetSoftDeletedPastRetentionAsync` (`EfSessionStoreTests`); old 60-min/empty-room immediate delete no longer fires; `GET /api/config` returns the configured windows; modal renders them.

## 16. Integrations status & how-to-connect help  `S`  ✅ done
- [x] Expanded the tracker modal (`case ('tracker')` in `session.page.html`) with an "Enabled integrations:" badge summary (always visible, gated to `enabledProviders()`) and a collapsible per-provider "How to connect" block next to the connect form.
- [x] Per-provider instructions content (PAT scope/where to generate, OAuth vs. token, base URL format) — Jira, Azure DevOps, GitHub, GitLab (`trackerHelp()` in `session.page.ts`).
- [x] Wired through i18n catalogs (en/es/pt/pl).
- [x] Tests: enabled-integrations summary lists only the enabled providers; instructions match the selected provider and switch when the selection changes.
---

# Platform phase — TeamTools with two tools

| # | Task | Effort | Depends on | Layer |
|---|------|--------|-----------|-------|
| 17 | `ARCHITECTURE.md` — document the platform target | S | — | Docs |
| 18 | Rename to TeamTools | M (mechanical) | 17 | All |
| 19 | Shared Room core | L | 17, 18 | BE (+FE contract) |
| 20 | Tool picker & platform shell | M | 19 | FE |
| 21 | Retro board: model, creation, cards | L | 19, 20 | BE + FE |
| 22 | Retro anonymity | S–M | 21 | BE + FE |
| 23 | Facilitator-driven phases | M | 21, 22 | BE + FE |
| 24 | Grouping into themes | M | 23 | BE + FE |
| 25 | Dot voting | M | 24 | BE + FE |
| 26 | Action items | M | 23 | BE + FE |
| 27 | Carry-over from the previous retro | S–M | 26 | BE + FE |
| 28 | Retro export | S–M | 24, 25, 26 | BE + FE |
| 29 | `README.md` & deploy | S | 17–28 | Docs |
| 30 | Password-guard the poker round history | S | 12, 28 | BE + FE |
| 31 | Get the frontend bundle back under its budget | S | 29 | FE |
| 32 | Join a room over its own tool's hub | S | 19, 31 | BE + FE |
| 33 | Narrow the retro phase-countdown sweep | S | 23 | BE |
| 34 | Extract the room-level primitives | S | 33 | BE |

> **Why this order.** The ease-before-dependents rule still applies, but four hard
> constraints dominate.
>
> **#17 (`ARCHITECTURE.md`) goes first, before any code moves.** #19 relocates nearly
> every type in Core and rewrites the schema with hand-written data motion — the one
> change where "document it afterwards" yields a refactor that drifted from the plan and
> a migration a reviewer cannot check against anything. Writing the target down first
> makes #19 an implementation of an agreed design, and finds a wrong design on a page
> instead of in a migration.
>
> **#18 (rename) goes next** even though it is not the easiest: every file #19–#28 adds
> should land with its final name, and renaming afterwards doubles the diff.
>
> **#19 (Room core) goes third** because it *is* the platform: a pure
> behaviour-preserving refactor guarded by the existing ≥90% Core coverage gate, and the
> one change that gets strictly harder the more retro code exists.
>
> **#22 (anonymity) comes before #23–#28, not after**, even though it looks like a
> settings toggle. Anonymity is a snapshot-contract property; build phases, grouping,
> voting and export against an attributed snapshot first and each becomes its own
> authorship leak to hunt down. Cheap now, expensive later.
>
> #23 (phases) then gates every later mutation, so grouping (#24) and voting (#25) build
> on it — and #25 follows #24 because dots attach to groups. #26 (actions) needs only the
> phase machine, so it can run in parallel with #24/#25. #27 needs actions to carry. #28
> (export) needs everything it serialises.
>
> **#29 (`README.md`) goes last** — the opposite deadline to #17. The README describes
> features as *shipped*, so rewriting it before the retro tool exists would advertise
> something a visitor cannot use. `ARCHITECTURE.md` describes the *design*, so it leads.

---

## 17. `ARCHITECTURE.md` — document the platform target  `S`  ✅ done
**First, before any code moves. #19 implements what this task decides.**
- [x] Rewrite `ARCHITECTURE.md` for the two-tool platform, stated as the target #18–#28 land on (the code catches up task by task).
- [x] Room / tool-payload boundary: which current `Session` fields go to `Room` and which to `PokerRound`.
- [x] Table shape after the split (`Rooms` + `PokerRounds` + the retro tables) and the requirement that the migration **moves** data rather than dropping it.
- [x] The `RoomSnapshot` fragment and the two-hub realtime contract (`PokerHub`, `RetroHub`).
- [x] One-tool-per-room rule; no team/workspace entity; #27 as the single deliberate cross-board link.
- [x] Which cross-cutting concerns are room-level and therefore inherited by both tools: #3, #4, #5, #6, #7, #15.
- [x] Keep the existing doc's structure and detail level where it still applies — this is a rewrite, not a blank page.
- [x] Verify: a reader can predict #19's file moves and migration from this doc alone. (Docs-only: no test run needed.)

## 18. Rename to TeamTools  `M (mechanical)`  — depends on #17  ✅ done
**Everything below landed with its final name.**
- [x] Renamed solution (`TeamTools.slnx`) + all `src`/test projects `PlanningPoker.*` → `TeamTools.*`; every `namespace`/`using` updated.
- [x] `PlanningPokerHub.cs` → `PokerHub.cs` (class too); `PlanningPokerDbContext` → `TeamToolsDbContext`.
- [x] EF migration snapshots renamed (`TeamToolsDbContextModelSnapshot.cs` ×3) and the designers' context references updated; `DesignTimeDbContextFactory.cs` ×3.
- [x] `Dockerfile`, `docker-compose.yml`, `.github/workflows/ci.yml`, `deploy/` (incl. the terraform systemd unit, which named `PlanningPoker.Api.dll` and would have broken the EC2 deploy), `run.sh`, `run.ps1`, `coverage-gate.ps1`, `.gitignore`.
- [x] Old default SQLite file handled: `LegacyDatabaseFile.ResolveDefaultConnectionString` keeps using `planningpoker.db` when it is the only one present and logs a rename hint; the new default is `teamtools.db`. Only applies when no connection string is configured.
- [x] Frontend `package.json` name, `index.html` title, `app.brand`/`app.copyright` in all four i18n catalogs, route titles; `NOTICE` product line.
- [x] **Not** `README.md` — that is #29.
- [x] Verify: backend 368 passed (364 pre-existing + 4 new `LegacyDatabaseFileTests`), frontend 115 passed, zero behavioural diff; `docker build` + container boot green with `/health` 200.

> **Deliberately not renamed** (each would be a destructive or out-of-scope change, not a rename):
> - `deploy/terraform/variables.tf` `app_name = "planning-poker"` — it is the name prefix for **all**
>   AWS resources, so changing it would destroy and recreate live infrastructure on the next apply.
> - the `pp-data` docker volume name — renaming it orphans the existing volume's SQLite data.
> - the compose project name (derives from the repo directory name) and the
>   `github.com/bizzehdee/plnpkr` URL — both are outside the codebase.
>
> The legacy `planningpoker.db` files in `src/TeamTools.Api/` are gitignored dev artifacts; they are
> exactly the case the fallback above covers.

## 19. Shared Room core  `L`  — depends on #17, #18  ✅ done
**The platform bet. Implements #17's design. Behaviour-preserving; no new features.**
- [x] `Session` → `Room` (short code, name, participants, presence, organiser set, password, `ClosedAt`/`DeletedAt`, reactions, `LastActivityAt`) + `RoomTool` discriminator (`Poker` | `Retro`), fixed at creation.
- [x] Poker-specific state → `Models/Poker/PokerRound` (`State`, deck, story + note, timer fields, tracker link/queue, `RoundResult`s), 1:1 with its room on a shared primary key.
- [x] `ISessionStore` → `IRoomStore`; the one poker-specific query moved to `IPokerRoundStore.GetRoomsWithExpiredTimerAsync`. `EfRoomStore` implements both (one `DbContext`, one unit of work); `FakeRoomStore` likewise.
- [x] `SessionService` → `RoomService` (join/leave/presence/role/organiser/password/close/delete) + `Poker/PokerService` (vote/reveal/reset/deck/story/note/timer/discussion/analytics), with `RoomAuthz` and `PokerRoundRules` holding the pure rules.
- [x] `SessionMaintenanceService` → room-level `RoomMaintenanceService` (purge + retention, returns rooms not snapshots) and poker-level `Poker/PokerTimerService` (round-timer expiry). Retention (#15) now applies to any room.
- [x] Room-scoped hub infra: `ConnectionRegistry`, `HubThrottle`, `ReactionRateLimiter` and the renamed `RoomEvictionService`, which projects each purged room through its own tool snapshot.
- [x] `RoomSnapshot` fragment embedded by `SessionSnapshot`; mirrored in `core/models.ts` as `RoomSnapshot` + `SessionSnapshotWire`; `realtime.client.ts` split into `room.client.ts` (transport, lifecycle, reactions, `flattenSession`) + `poker.client.ts` (poker hub methods).
- [x] EF migration ×3 that **moves** existing rows into `Rooms` + `PokerRounds` — hand-written data motion, plus a lossless `Down`.
- [x] Verify: backend **386** tests pass (368 + 14 `RoomCoreTests` + 4 migration tests), frontend **118** (115 + 3 `flattenSession`), coverage gate **94.5% line / 90.5% branch** (≥90%), `docker build` + container boot with `/health` 200 and `/api/config` serving.

> **The scaffolded migration would have destroyed every session.** Two separate data-loss bugs were
> found and fixed here, both by the pre-refactor-upgrade test rather than by reading the diff:
> 1. EF's scaffold answered the table split with `DropTable("Sessions")` + two empty `CreateTable`s.
> 2. After hand-writing the data motion, the SQLite run *still* lost every participant seat and
>    round-history row: SQLite cannot alter a foreign key in place, so EF defers those tables to a
>    rebuild emitted at the **end** of the migration while hoisting `DROP TABLE "Sessions"` **above**
>    it — and with foreign keys enforced, SQLite's `DROP TABLE` fires the children's `ON DELETE
>    CASCADE` first. (EF's own `PRAGMA foreign_keys = 0` around the rebuild is a no-op inside the
>    migration transaction.) The SQLite migration therefore rebuilds the child tables itself, in
>    order, and drops `Sessions` only once nothing references it.
>
> `SplitRoomMigrationTests` seeds a database **under the old schema** and asserts the room, its
> seats (including a mid-round vote), the round state, the story note and the round history all
> survive — plus a lossless downgrade. SQL Server and PostgreSQL use in-place `ALTER TABLE`
> constraint changes and have no cascade-on-drop semantics, so they keep the straightforward
> operation order; they are **not** covered by an executed test here (no server available locally),
> which is the one gap in this task's verification.

> **Divergence from #17's design, corrected in `ARCHITECTURE.md` rather than left silent.** The doc
> specified `TeamTools.Poker` / `TeamTools.Retro` as separate *projects*, to make "the tools never
> reference each other" a compile-time guarantee. That is not purchasable at this price: room-level
> changes must re-evaluate a tool's completion gate (making someone an observer can complete a poker
> round), which across assemblies needs an event-port indirection in `Core`, and one EF `DbContext`
> must own every tool's entities regardless. The tools are sibling namespaces inside
> `TeamTools.Core` instead (`Core/Poker/`, `Core/Retro/`), with the coupling made explicit as a
> **tool hook**: `RoomService` takes an `afterChange` callback that the tool supplies. The rule is
> now review-enforced, not compiler-enforced — stated as such in the doc.

## 20. Tool picker & platform shell  `M`  — depends on #19  ✅ done
- [x] Home page → platform picker (`pages/home`); the poker create form moved to `pages/poker/create` (`PokerCreatePage`) at `/poker/new`.
- [x] Routes: `/poker/new`, `/poker/:shortCode`, `/join/:shortCode`, picker at `/`; `/session/:shortCode` kept as a permanent redirect so existing invite links survive. Retro routes land in #21.
- [x] `join.page.ts` reads `landing.tool` (per #19) and navigates to that tool's page instead of assuming poker.
- [x] Shared shell in `app.html`: brand through `app.brand`, theme toggle, language switcher, plus an "All tools" switcher shown on every page except the picker itself.
- [x] a11y + i18n: the tools render as a real list (so their number is announced), and the unbuilt tool is a **disabled button with an `aria-describedby` explanation** rather than a dead anchor — a styled-dead link is unreachable by keyboard and announces nothing. All 20 new strings in en/es/pt/pl.
- [x] Tests: picker offers both tools and links poker to `/poker/new`; the retro card is unavailable rather than a dead link; legacy `/session/:code` redirect; unknown paths return to the picker; join routes by tool (`Retro` → `/retro`). Frontend **127** passing.
- [x] Verified in a browser: picker renders both cards, and `/session/blue-fox-42` redirects through `/poker/…` to the join gate.

> **Team Retro is listed but not startable.** Its card carries a "Coming soon" badge and a disabled
> action until #21 ships the tool. Listing it makes the platform's shape clear; linking it would
> advertise something a visitor cannot use.

## 21. Retro board: model, creation, cards  `L`  — depends on #19, #20  ✅ done
**Foundation for #22–#28.**
- [x] `Core/Models/Retro/RetroBoard.cs` (`RetroBoard`, `RetroColumn`, `RetroCard`) + `Core/Retro/RetroTemplateCatalog.cs` (Went well/To improve/Actions, Start-Stop-Continue, 4Ls, Mad-Sad-Glad, Custom — mirroring `DeckCatalog`) + `Core/Retro/RetroService.cs`. Columns are **materialised** from the template at creation, not resolved per read, because a card belongs to a column and a custom layout has to persist.
- [x] `Contracts/RetroSnapshots.cs` embedding `RoomSnapshot`; mirrored in `core/models.ts` as `RetroBoardSnapshotWire` + the flat `RetroBoardSnapshot`.
- [x] `RetroHub`: `AddCard`, `EditCard`, `DeleteCard`, `MoveCard`, `SetTemplate` (+ the room-level surface); author-or-organiser authz on edit/delete/move, so a facilitator can moderate.
- [x] Card text capped at 500 chars (`RetroService.MaxCardLength`, enforced server-side and mirrored as the input's `maxlength`) and adds rate-limited by a new `HubThrottle.TryAddCard` window (#3) — cards are the one high-frequency write on a board.
- [x] EF migration ×3 (purely additive: `RetroBoard`, `RetroColumn`, `RetroCard`).
- [x] `pages/retro/create` + `pages/retro/board` + `core/retro.client.ts` (`flattenBoard`, sharing `RoomClientBase` with poker); the picker's retro card is now a live link.
- [x] a11y + i18n: columns and cards render as nested lists (so counts are announced), changes go through an `aria-live` region, and **every card carries a keyboard "move to column" select** — the drag-and-drop in #24 lands on top of that, never instead of it. 47 strings × en/es/pt/pl.
- [x] Tests: 49 new backend (`RetroBoardTests`, `RetroTemplateCatalogTests`, `RetroRoomOperationsTests`, 3 in `EfRoomStoreTests`) and 14 new frontend (`retro.page.spec.ts`). Backend **455**, frontend **141**, coverage gate **94.6% line / 90.9% branch**.

> **Broadcasts are per connection, not per group.** A retro snapshot depends on who receives it —
> authorship under anonymity (#22), hidden collection (#23) — so `RetroHub.BroadcastBoard` projects
> the board per viewer and sends each connection its own copy, using a new
> `ConnectionRegistry.InRoom`. That is the cost of making those properties of the wire rather than
> of the UI, and it is why the retro event is `BoardUpdated` per client rather than a group send.

> **Two bugs the unit tests could not have found**, both caught by driving the real app in a browser:
> 1. **Every card add failed** with `DbUpdateConcurrencyException`. `RetroService` assigns card ids
>    itself (it is pure, and the in-memory fake generates nothing), and without
>    `ValueGeneratedNever()` EF reads a non-default `Guid` key on an entity added to an
>    *already-tracked* room as proof the row exists — so it issued an `UPDATE` matching zero rows
>    instead of an insert. Board *creation* hid it, because a brand-new graph is `Added` wholesale.
>    Now configured explicitly and covered by an `EfRoomStoreTests` regression against real SQLite.
> 2. **Creating a board bounced the facilitator to the join gate**, because the create flow never
>    remembered the seat. The board page now short-circuits when it already holds the board (the
>    poker table's rule) and remembers `shortCode → role` through an effect.
>
> Also fixed a smaller thing the split introduced: the shell's connection badge watched only the
> poker client, so it read "disconnected" on a live retro board. It now reports whichever tool
> client is actually connected.

## 22. Retro anonymity  `S–M`  — depends on #21  ✅ done
**Before #23–#28: it is a snapshot-contract property, not a UI toggle.**
- [x] `RetroBoard.Anonymous` (also settable at creation) + `RetroHub.SetAnonymous`, organiser-gated, refused once the first card exists.
- [x] Per-recipient projection: on an anonymous board `AuthorUserId` **and** `AuthorDisplayName` are null for **every** card — including the recipient's own — with `IsMine` carrying ownership so authors can still edit.
- [x] Documented on the model, the contract and in the UI copy that this is anonymity **from participants, not from a database administrator**: `AuthorUserId` is still stored, because an author must be able to edit their card and a facilitator needs a moderation target.
- [x] EF migration ×3 (single additive column).
- [x] i18n: the create-form option and its "locks once the first card is added" help, the board badge with a tooltip stating the limit of the guarantee, the toggle, the locked error and two announcements — ×4 locales.
- [x] Tests: 15 backend (`RetroAnonymityTests`) + 5 frontend. Backend **469**, frontend **146**, coverage gate **94.7% line / 91.2% branch**.

> **Why null for everyone rather than "null for everyone but you".** Sending the author their own
> id would keep a userId on the wire for exactly the person it identifies, and every later feature
> (grouping, dot voting, export) would have to remember not to widen that. `IsMine` carries
> ownership instead, so there is no authorship field left to leak.

> **The lock cuts both ways.** Turning anonymity *off* would expose cards written under a promise of
> anonymity; turning it *on* would retroactively hide attributed ones. Both change what people
> agreed to when they wrote them, so both are refused once any card exists — the facilitator
> included. The snapshot carries `CanChangeAnonymity` so the UI hides the control instead of
> offering one that fails when used.

> **A test caught a wrong test, not wrong code.** The first version asserted the serialized snapshot
> contained no occurrence of the author's name anywhere — and failed, because the *participant list*
> legitimately names everyone in the room. Who is in the retro is not secret; who wrote which card
> is. The assertion is now scoped to the cards, which is where the guarantee actually lives.

## 23. Facilitator-driven phases  `M`  — depends on #21, #22  ✅ done
- [x] `RetroPhase` (Collect → Group → Vote → Discuss → Actions → Closed) + `RetroPhaseRules` (pure) and `AdvancePhaseAsync` / `PreviousPhaseAsync` / `SetPhaseAsync` in `RetroService`. Forward-only in normal use, with an organiser-only step **back one phase** — facilitators mis-click, and the alternative is a retro stuck in the wrong phase. Arbitrary jumps are refused.
- [x] `RetroHub.AdvancePhase`/`PreviousPhase`/`SetPhase`/`SetPhaseDuration`, organiser-gated (#7).
- [x] Collect hides other participants' cards **in the projection**; each column reports a `HiddenCardCount` so a participant knows the team is writing without seeing what.
- [x] Phase-gated mutations: cards are add/edit-able only during Collect (a late card would invalidate the grouping and tallies built on the ones already there), while move and delete stay open — that is how a facilitator tidies during Group.
- [x] `RetroPhaseTimerService` + `RetroPhaseTimerBackgroundService` reuse the poker round timer's deadline-broadcast pattern (#14/#9): one server-authoritative instant, clients ticking locally, a 1s sweep clearing elapsed ones.
- [x] EF migration ×3 (three additive columns).
- [x] a11y + i18n: the rail is an ordered list with `aria-current="step"` (so the sequence is announced, not just styled), phase changes go through the `aria-live` region, and the composer and edit control disappear when the phase forbids them rather than failing on use. 15 strings × 4 locales.
- [x] Tests: 24 backend (`RetroPhaseTests`) + 10 frontend. Backend **493**, frontend **156**, coverage gate **94.6% line / 90.9% branch**.

> **An elapsed countdown clears the timer; it does not advance the phase.** A retro is facilitated:
> time running out is a prompt for the person running it, not a reason to move a room full of people
> on mid-sentence. Poker can auto-reveal because a reveal is mechanical — "we are done grouping" is
> a judgement call.

> **The scaffolded migration would have broken every existing board.** `Phase` is a string-converted
> enum, and EF's `AddColumn` default for a non-nullable string is `""` — unparseable, so every board
> created under #21/#22 would have failed to load. Hand-set to `Collect` in all three providers: a
> board that predates phases is, by definition, still collecting.

> **Eight existing tests started failing, and they were right to.** They added a card as one
> participant and read it as another — which hidden collection now forbids. The fix was to their
> preconditions (end collection first), not to the feature: cross-participant visibility is only
> meaningful once collecting has ended.

## 24. Grouping into themes  `M`  — depends on #23  ✅ done
- [x] `RetroGroup` + `RetroCard.GroupId`; `RetroHub.GroupCards` / `UngroupCard` / `RenameGroup` / `SetAllowParticipantGrouping`.
- [x] Organiser-driven by default, with `RetroBoard.AllowParticipantGrouping` to open it to everyone — off by default because grouping is a facilitation act, and two people dragging the same card in opposite directions is worse than waiting.
- [x] Concurrent drags resolve last-write-wins on `GroupId`, reconciled by the per-recipient board rebroadcast; cheaper and less surprising than locking at room scale.
- [x] EF migration ×3; snapshot gains `Groups` + `RetroCardInfo.GroupId`, mirrored in `core/models.ts`.
- [x] Drag-and-drop in `retro.page.*` (card→theme, card→card forms a pair) **plus** a keyboard "group with…" select on every ungrouped card and a "remove from theme" button on every grouped one. The select lists existing themes and "New theme"; the drag handlers are the addition for mouse users, never the only way in (#4).
- [x] i18n: 11 strings × 4 locales.
- [x] Tests: 22 backend (`RetroGroupingTests`) + 12 frontend. Backend **515**, frontend **168**, coverage gate **94.8% line / 90.9% branch**.

> **Themes carry the same per-recipient card projection as the columns**, so grouping cannot become
> a side door around anonymity (#22) or hidden collection (#23) — a test asserts an anonymous
> board's theme cards carry no authorship. Cards appear in both their column and their theme, so a
> client can render the board either way without a second request.

> **Grouping is refused outside the Group phase**, in both directions: during Collect the cards are
> still hidden from each other, so grouping them is meaningless; from Vote onwards, regrouping would
> move dots people have already spent.

> **An emptied theme is removed, not left behind.** Every grouping change prunes groups that hold no
> cards and renumbers the rest — an empty theme is not something the team can discuss or vote on.
> The relationship is `SetNull` rather than `Cascade`: deleting a theme must never take the team's
> cards with it.

## 25. Dot voting  `M`  — depends on #24  ✅ done
- [x] `RetroVote` (`{ Id, BoardId, VoterUserId, TargetKind, TargetId }`) — **one row per dot**, so withdrawing is a row delete and the budget is a row count. No arithmetic to get wrong, and no client-supplied total to be believed.
- [x] `RetroBoard.VoteBudget` (default 3) / `AllowMultiplePerItem` (off by default — spreading dots surfaces more of what the team cares about), settable by an organiser and clamped to 1–20.
- [x] `RetroHub.CastRetroVote` / `WithdrawVote` / `SetVoteBudget`, with the budget and the no-stacking rule enforced **server-side from the stored rows**.
- [x] `RetroTallyCalculator` (pure, the `StatsCalculator` sibling): own dots, totals, and the ranked agenda with ties broken on display order so the ranking is stable rather than arbitrary.
- [x] Own dots always visible; totals and the ranking withheld until Discuss — a running total tells people where to put their remaining dots, the same anchoring argument as hidden collection (#23). A voter may only withdraw their own dot.
- [x] EF migration ×3; snapshot + `core/models.ts`.
- [x] a11y + i18n: every dot button carries an `aria-label` naming its item (a bare "+" tells a screen-reader user nothing about what they are voting for), remaining allowance announced after each dot, and the controls disable exactly where the server would refuse. 13 strings × 4 locales.
- [x] Tests: 26 backend (`RetroVotingTests`) + 11 frontend. Backend **541**, frontend **179**, coverage gate **95.0% line / 91.2% branch**.

> **Grouping needs no vote migration at all.** A theme's total is *its own dots plus the dots on the
> cards inside it*. Grouping voted cards therefore sums them automatically; ungrouping hands each
> card its own dots back, because they never moved. That matters because #23 lets a facilitator step
> back from Vote to Group — so regrouping mid-retro is reachable, and it must not silently destroy
> votes. Two tests cover exactly that round trip.

> **Cards inside a theme are not listed separately in the ranking.** The theme is the unit of
> discussion; listing its cards too would double-count the same dots.

> **The scaffolded migration would have left existing boards unvotable**, defaulting `VoteBudget` to
> 0. Hand-set to 3 in all three providers — the same class of bug as #23's `Phase` default, found
> the same way: by reading the generated migration rather than trusting it.

## 26. Action items  `M`  — depends on #23  ✅ done
- [x] `RetroActionItem` (`{ Id, BoardId, Title, OwnerUserId, OwnerName, DueDate, DoneAt, SourceGroupId?, CarriedFromBoardId? }`).
- [x] `RetroHub.AddAction` / `EditAction` / `ToggleActionDone` / `DeleteAction`; creatable from a theme (the UI prefills the title from the theme label, and the action keeps `SourceGroupId` for provenance) or standalone.
- [x] Optional, free-text-capable owner: picking a participant stores their **display name** as well as their id, so the action still reads correctly after they leave the room or are evicted from it; typing a name covers an owner who was never at the retro, since the platform has no accounts.
- [x] Explicit carve-out in the close check: `LoadForActionWriteAsync` deliberately omits the `ClosedAt` test, so actions stay writable on a closed board.
- [x] EF migration ×3; snapshot + `core/models.ts`; actions panel in `retro.page.*`, with outstanding actions listed before finished ones and due dates before undated.
- [x] a11y + i18n: due dates through a new `I18nService.formatDate` (`Intl.DateTimeFormat`, UTC — date order is not universal and a timezone shift would slide a date-only due date by a day), the done checkbox carries an `aria-label` naming its action, and every change is announced. 21 strings × 4 locales.
- [x] Tests: 26 backend (`RetroActionTests`) + 11 frontend. Backend **567**, frontend **190**, coverage gate **95.0% line / 91.2% branch**.

> **The carve-out is tested for what it does *not* allow.** The whole risk of an exception like this
> is that it widens, so `The_carve_out_covers_actions_and_nothing_else` asserts that on a closed
> board adding, editing, deleting and grouping cards, casting a dot, moving the phase, changing
> anonymity and changing the template all still return `BoardClosed`. A soft-deleted board is gone
> from every read regardless — carve-out or not.

> **Two tests were wrong, not the code.** One asserted "no primary-outline button on a closed
> board", which now also matches the deliberately-present *action* composer; it is scoped to the
> columns. The other asserted the words "Action items" were absent before the discussion — but the
> default template has a *column* called "Action items", so it would have passed or failed for the
> wrong reason. It now asserts on the panel's heading element.

## 27. Carry-over from the previous retro  `S–M`  — depends on #26  ✅ done
- [x] Retro creation accepts a previous board's short code (`CreateRetroRequest.PreviousBoardShortCode`); the new board records it in `RetroBoard.PreviousBoardShortCode` for provenance.
- [x] `ResolveCarryOverAsync` **copies** the not-done actions, keeping `CarriedFromBoardId`, so the new board stays self-contained for export (#28) and survives the old one's retention delete (#15). Deliberately *not* copied: `DoneAt` (unfinished by definition) and `SourceGroupId` (that theme belongs to the old board's cards).
- [x] Requires the previous board's password if it had one — a short code is a bearer token, and carry-over must not become a way to read a protected board's commitments, least of all an anonymous one's.
- [x] Carried actions appear in the actions panel from the start of Collect, badged "carried over"; a closed board offers a **deep-linked** "Start the next retro" that prefills its own code via `?from=`.
- [x] EF migration ×3 (one additive column).
- [x] i18n: 9 strings × 4 locales.
- [x] Tests: 18 backend (`RetroCarryOverTests`) + 5 frontend. Backend **585**, frontend **195**, coverage gate **95.1% line / 91.4% branch**.

> **Carry-over is resolved before the new room is created**, so a wrong password or an unknown code
> fails without leaving a half-made retro behind — there is a test that counts the rooms.

> **An unknown previous code is an error, not a silent no-op.** Creating the board anyway with
> nothing carried would look exactly like a team that had finished everything.

> **The review list needed a UI fix the server had already got right.** Adding a *new* action is
> phase-gated to Discuss/Actions, but ticking an *existing* one is not — carried actions are
> reviewed during Collect, and "mark done" happens days later on a closed board. The page had one
> predicate for both and so hid the checkbox exactly when the review list was being reviewed; it
> now has `canWriteActions` (add) and `canUpdateActions` (tick/edit/remove), matching the service.

## 28. Retro export  `S–M`  — depends on #24, #25, #26  ✅ done
- [x] `POST /api/retro/{shortCode}/export` (`RetroController`), body `{ format: md|csv|json, password }`, reusing #12's content-type and file-download handling.
- [x] `RetroService.GetExportAsync` projects the whole board — columns, cards, themes with dot tallies, action items — into `RetroExport`; `RetroExportRenderer` renders Markdown (grouped by theme, ordered by dots, actions as a task list), CSV (a row per card and per action, RFC-4180 quoted) and JSON.
- [x] **Anonymity holds in the export** (#22): an anonymous board carries no author in any format, the CSV drops the Author *column* entirely rather than blanking it, the Markdown says the cards were anonymous, and `GetExportAsync` takes **no caller identity** — so there is no organiser override to write.
- [x] Two guards, both deliberate: the board's **password**, and a **phase** check (`RetroPhaseRules.VoteTotalsVisible`) so export cannot be a side door around hidden collection (#23) or the withheld dot totals (#25).
- [x] Read-only post-retro summary view at `/retro/{code}/summary` + a download button (md/csv/json) on the board, offered only once the totals are visible.
- [x] i18n: 23 strings × 4 locales.
- [x] Tests: 28 backend core (`RetroExportTests`) + 7 API end-to-end (`RetroExportEndpointTests`) + 19 frontend. Backend **620**, frontend **214**, coverage gate **95.5% line / 91.7% branch**.

> **A POST, not the `GET` the plan specified.** A protected board's export needs its password, and
> a password in a query string ends up in server logs, proxy logs and browser history. The body
> keeps it out of all three. The cost is that the download has to be triggered from script rather
> than a plain `<a download>` link, so the client fetches the file and hands it over as an object
> URL — the SPA was already doing the fetch for the summary view.

> **The plan said to reuse #12's "existence-plus-password guard" verbatim. There is no password
> guard in #12 to reuse.** The poker export
> ([`SessionsController.Export`](backend/src/TeamTools.Api/Controllers/SessionsController.cs)) is a
> `GET` guarded by session existence alone, so anyone holding a short code can download a
> protected session's whole round history. The retro export does not copy that: it verifies the
> password. Poker's gap is pre-existing and out of scope here, but it is real and worth its own
> task.

> **The export needed a phase guard the plan did not ask for.** Without one, #23 and #25 would
> both have a trivial bypass: during Collect the export would hand out every card the team has not
> seen yet, and during Vote it would hand out the running dot totals that are deliberately
> withheld. Export now starts at Discuss. The board simply does not offer the button before then,
> so the refusal is a backstop rather than the normal path.

> **The summary view is not the board in read-only mode.** Opening the board joins the room, which
> puts a name in the participant list and takes a seat. Someone reading last week's outcome — the
> person an action was assigned to, a manager, a late joiner — should not have to become a
> participant to do it, so the summary page reads the export instead: no hub connection, no seat,
> no identity sent, and the same anonymity guarantee as the file.

> **The export JSON is camelCase, unlike #12's.** The poker export builds its own
> `JsonSerializerOptions` and so ships PascalCase; nothing parses it. This payload *is* parsed —
> the summary view reads it directly — so it uses the platform's own wire casing and named enums,
> via a single `RetroExportRenderer.ToJson` shared by the controller and the tests.

## 29. `README.md` & deploy  `S`  — depends on #17–#28  ✅ done
**Last: the README describes shipped features, so it waited until they were.**
- [x] `README.md` rewritten around the platform: intro, a feature list per tool, a "shared by both tools" list, the new solution layout, and corrected project/db/test names throughout (it still said `PlanningPoker.Api`, `planningpoker.db` and 303 tests).
- [x] `ARCHITECTURE.md`: corrections only, each marked **Design correction** and saying what changed — singular child-table names, the retro cascade choices, the SQLite table-rebuild trap, hand-set migration defaults, per-connection retro broadcast, the absent reducers, `pages/session/`, `__PP_CONFIG__`, and the #12 password gap. Plus a new REST-surface subsection, since #28 added the platform's second export.
- [x] CI: the Core coverage gate now runs in `.github/workflows/ci.yml`, not only locally — the README had been promising for several tasks that CI ran "the same checks".
- [x] `deploy/terraform`: `app_name` follows the rename (`teamtools`), with a loud note in both `variables.tf` and `terraform.tfvars.example` that an existing stack must pin the old value.
- [x] `NOTICE`: the required-credit wording still said "Based on plnpkr" under a header that already said TeamTools.
- [x] One commit, docs-only. Per the repo convention, no test run to source a number — the numbers come from #28's run.

> **The README claimed CI/CD and IaC were out of scope.** Both had since landed (`.github/workflows/ci.yml`,
> `deploy/`), so the deploy section is now organised by target — AWS via Terraform, Azure App Service —
> behind the three constraints that hold for any of them (WebSockets, no idle shutdown, single instance).

> **`window.__PP_CONFIG__` and `pages/session/` keep their old names on purpose,** and both are now
> documented as decisions rather than left looking like misses. The config global is read from a
> `config.js` that lives in *deployed* static files: renaming it would make any deployment whose
> `config.js` was not updated in the same breath as the bundle fall back silently to same-origin. The
> folder is only a folder — the route moved to `/poker/:shortCode` in #20 — and renaming it would touch
> every import for no behavioural gain.

> **One thing deliberately left undone at the time: the poker export had no password guard** (#12 —
> `GET /api/sessions/{shortCode}/export` checked only that the session existed, so a short code alone
> downloaded a protected session's whole round history). Found while implementing #28, which did not
> copy the gap. Fixing it was a behavioural change to a shipped feature rather than a docs task, so it
> was recorded here instead of being smuggled into the last commit — and is now **closed by #30**.

> **The bundle was over its 800 kB budget (906 kB).** Pre-existing and unrelated to #28/#29 — it was
> 889 kB before the export UI. Worth a task of its own (lazy-load the two tools' routes) rather than a
> silently raised budget — now **closed by #31**, at 690 kB.

## 30. Password-guard the poker round history  `S`  — follow-up to #12/#28  ✅ done
**Found while implementing #28, deferred at #29, closed here.**
- [x] `PokerService.GetAnalyticsAsync` / `GetAnalyticsCsvAsync` take a password and return a `SessionExportStatus` (`Ok` / `SessionNotFound` / `PasswordRequired`).
- [x] `RoomService.VerifyPassword` — one verdict on the room engine, shared by the join gate (#2), the retro export (#28) and this. `RetroService` dropped its own `IPasswordHasher` in the process.
- [x] `POST /api/sessions/{code}/export` and `POST /api/sessions/{code}/analytics`: 404 unknown, 403 missing/wrong password, 200 otherwise.
- [x] Frontend: the POST-and-object-URL transport extracted to `core/export-transport.ts` and shared by both tools; a new `SessionExportService`; the analytics modal and the export buttons ask for the password **only after the server refuses**.
- [x] i18n: 2 strings × 4 locales.
- [x] Tests: 5 core (`AnalyticsTests`) + 6 API end-to-end (`ExportEndpointTests`) + 5 frontend. Backend **633**, frontend **219**, coverage gate **95.5% line / 91.7% branch**.

> **The analytics read is guarded too, not just the file download.** It returns a strict superset of
> the CSV — the same stories, notes and estimates — so guarding one and not the other would be
> theatre.

> **The `/join` landing read stays open on purpose.** It is how the join page learns a password is
> needed at all, and it carries nothing but the room name and that fact. There is a test for it in
> both projects, so a later tightening pass does not close it by reflex.

> **GET → POST is a breaking API change, taken deliberately.** The SPA ships from the same artifact
> and changes in lockstep, and a GET that can carry a password in its query string is exactly what
> #28 refused. No GET alias was left behind: one would either have to refuse every protected session
> anyway, or accept the password in the URL and undo the guard. `ExportEndpointTests` asserts the
> route no longer answers a GET with a password in its query string.

> **#12's export JSON keeps its PascalCase casing.** It was tempting to align it with #28's
> camelCase while touching the file, but nothing parses that payload — changing a shipped file
> format to match a convention no reader applies would be churn, not consistency.

## 31. Get the frontend bundle back under its budget  `S`  — follow-up to #29  ✅ done
**Recorded as over-budget at #29, closed here. Measured after each change, not once at the end.**

| | initial raw | transfer |
| --- | --- | --- |
| before | **909.75 kB** | 181.54 kB |
| + lazy tool routes | 775.40 kB | 161.43 kB |
| + connection-status registry | 770.68 kB | 160.12 kB |
| + drop Bootstrap's JS | **690.23 kB** | **138.52 kB** |

- [x] `loadComponent` for the five tool pages; the picker and `/join/:shortCode` stay **eager** as the two cold entry points. Largest lazy chunk: the poker table at 74.9 kB.
- [x] `core/connection-status.service.ts`: each tool client registers its status as it is constructed, so the shell's badge no longer injects both clients. The shell no longer knows how many tools exist.
- [x] Dropped `bootstrap.bundle.min.js` from `angular.json` — **nothing used it**. No `data-bs-toggle`/`-dismiss`/`-target` anywhere, and no `bootstrap.*` JS API call; the only `data-bs-` in the repo is `data-bs-theme`, which is CSS. 80.45 kB raw / 21.60 kB transfer of dead weight.
- [x] Budget left at 800 kB. Headroom is now ~110 kB.
- [x] Verified in a real browser as well as by the suite: modal open, Esc-to-close, the header dropdown, the theme toggle, and the connection badge reading "connected" through the new registry. The shell spec grew from 3 specs to 6 — see below. 222 frontend specs.

> **The Bootstrap JS bundle was 12% of the initial payload and was never called.** Every modal,
> dropdown and collapse in this app is signal-driven markup — which is *why* the session page has a
> hand-written `@HostListener('document:keydown.escape')`. Bootstrap's CSS is still needed and still
> loaded; only the JS went.

> **`@microsoft/signalr` was still in the initial bundle (57 kB) when #31 finished.** Lazy routes
> moved both tool clients out, and the shell no longer pins them — but `/join/:shortCode` is eager
> and joined the room over the poker hub before navigating, so it pulled the transport back in.
> Making the whole join page lazy would have traded a round-trip on the *high-intent* path (an invite
> link) for bytes on the browsing path, which is the wrong direction, so it was left alone here.
>
> Noticed while looking: the join page joined **every** room over the *poker* hub, including retro
> rooms. That turned out to be a real bug, not just redundancy — **#32** fixes it, and resolving the
> client per tool moved the transport out of the initial bundle after all (631 kB).

> **The i18n catalogs were left eager, on the numbers.** Four locales inline are 100 kB of source,
> and splitting the three non-English ones behind dynamic imports was the obvious next cut. But
> translation strings compress: those three are **76.3 kB raw and 17.9 kB gzipped**. Buying ~18 kB of
> transfer would mean making `setLocale` async, awaiting a chunk before bootstrap for non-English
> users, and giving the app's most-depended-on service a new failure mode (a failed fetch silently
> renders English). Bad trade at this size. Revisit if a fifth and sixth locale land.

> **The shell spec was passing for the wrong reason, and the registry exposed it.** It provided a
> fake poker client and asserted the page's whole `textContent` contained `"connected"` — which is
> true of `"disconnected"` too, and of `"desconectado"` for `"conectado"`. So it would have passed
> whatever the badge said. It now asserts the badge element's exact text (via a new
> `id="connection-status"`), and covers all three states including the multi-tool fold.

## 32. Join a room over its own tool's hub  `S`  — follow-up to #19/#31  ✅ done
**Noticed at #31, closed here. Reproduced in a browser first, then fixed both halves.**
- [x] **Client:** `join.page.ts` resolves the hub client from the landing read's tool instead of always using poker's. The client is `import()`ed once the tool is known, so the page holds no compile-time knowledge of either tool.
- [x] **Server:** `RoomService.JoinAsync` takes the calling service's tool and refuses a mismatch with a new `JoinStatus.WrongTool` — **before writing anything**. Both tool services pass their own, so the rule lives in the room engine and a third tool would inherit it.
- [x] `RoomClientBase.joinRoom` is the room-level join contract both clients implement (each tool's `joinSession`/`joinBoard` stays for callers that want the snapshot). `JoinStatus` was already shared, which is what made one contract possible.
- [x] i18n: 1 string × 4 locales.
- [x] Tests: 5 core (`RoomCoreTests`) + 3 frontend, plus the join spec's harness fixed. Backend **638**, frontend **226**, coverage gate **95.5% line / 91.7% branch**.
- [x] Verified end to end in a browser: a retro invite link now lands on the board with the badge reading "connected", and a poker invite link still lands on the table.

> **Reproduced before fixing.** Joining a retro room from its invite link showed *"Could not reach
> the server. Is the API running?"* — while the API was up and the shell's own badge said
> "connected". The API log had the real story:
> `InvalidOperationException: Room 'red-moose-36' hosts Retro, not Poker — it has no poker round.`

> **The seat was taken anyway, which was the damaging half.** `RoomService.JoinAsync` commits the
> participant, and only then does `PokerService` project a snapshot and throw. So the joiner was in
> the room, told they were not, and `membership.remember` never ran — the one state the client cannot
> recover from. There is a test that counts the participants after a refused cross-tool join.

> **Fixed in both places on purpose.** The client fix alone would have been enough to make invite
> links work, and the server fix alone would have turned a 500 into a clean refusal. Neither alone is
> the guarantee worth having: a tool service must not be *able* to seat someone in another tool's
> room, whatever the client does, and the client should not need a server refusal to route correctly.

> **This is what #31 could not move out of the initial bundle.** The eager join page pinned
> `@microsoft/signalr` there by injecting the poker client. Resolving the client on demand moved both
> clients and the transport into lazy chunks: **690.23 kB → 631.04 kB initial** (138.52 → 126.66 kB
> transfer). The page's first act is an HTTP read, so there is nothing to overlap with — the import
> happens when the user presses Join.

> **`RoomService.JoinAsync`'s tool argument is optional, and stays optional.** Nothing tool-agnostic
> joins today, but the room engine does not get to assume every caller is a tool — that assumption is
> exactly the direction #19 was pulling away from. There is a test pinning the tool-agnostic call.

> **The join spec's harness was reading a half-initialised component.** It called `join()` without
> waiting for the landing read, so `tool()` was still its `'Poker'` default when the client was
> resolved while the later navigation read the resolved `'Retro'`. The old assertions could not see
> the difference — they only checked where it navigated. The harness now awaits stability, which is
> what the real page does by gating the form on `loading()`.

## 33. Narrow the retro phase-countdown sweep  `S`  — follow-up to #23  ✅ done
**Found by reading the EF command log of an idle server: two queries a second, one of them expensive.**
- [x] New `IRetroBoardStore.GetRoomsWithExpiredPhaseAsync` — the retro sibling of `IPokerRoundStore`, kept off `IRoomStore` so the room engine carries no knowledge of countdowns. `EfRoomStore` now implements all three ports.
- [x] `RetroPhaseTimerService` asks the store which boards are due instead of asking for every room and deciding in memory.
- [x] Board only, **no collection includes**: the sweep clears the deadline and saves, and the background service re-reads each board per recipient anyway (#21).
- [x] `FakeRoomStore` mirrors the query, as it already does for the poker sweep.
- [x] Tests: 5 in `EfRoomStoreTests` — the narrowing, poker rooms ignored, soft-deleted rooms ignored, the collections **not** loaded, and that the entities come back tracked so clearing the deadline persists. Backend **643**, coverage gate **95.4% line / 91.6% branch**.

> **What it was.** `RetroPhaseTimerService` called `IRoomStore.GetAllAsync()`, which is
> `WithPayload(_db.Rooms)` — a nine-way `LEFT JOIN` across `Rooms`, `PokerRounds`, `RetroBoard`,
> `Participants`, `RoundResult`, `RetroColumn`, `RetroCard`, `RetroGroup`, `RetroVote` and
> `RetroActionItem`, with `WHERE "r"."DeletedAt" IS NULL` as its only narrowing — then filtered
> `PhaseDeadline <= now` in memory. Once per second, forever, running or not.

```
- FROM "Rooms" LEFT JOIN PokerRounds, RetroBoard, Participants, RoundResult,
-   RetroColumn, RetroCard, RetroGroup, RetroVote, RetroActionItem
-   WHERE "r"."DeletedAt" IS NULL                                  -- every room, 5 collections
+ FROM "Rooms" LEFT JOIN "RetroBoard"
+   WHERE "r"."DeletedAt" IS NULL AND "r0"."RoomId" IS NOT NULL
+     AND "r0"."PhaseDeadline" IS NOT NULL                         -- only boards mid-countdown
```

> **Honest about the size of the win: on an idle dev database, invisible.** Both queries measure
> 0–1 ms there, and the tick rate is unchanged at two queries a second — this fixes *what* each tick
> costs, not how often it happens. The old query's cost grew with every room ever created and
> multiplied across five collections; the new one returns one row per board with a countdown running,
> which is normally none. It is a scaling fix, and the reason to make it now is that it was free.

> **It also silenced EF's `MultipleCollectionIncludeWarning` for this query.** Five collection
> includes in one statement fan out into a near-cartesian result set; EF logs the warning once per
> query shape. Two remain, both from `WithPayload`'s legitimate callers — idle eviction and the
> retention purge, which genuinely need the whole graph in order to delete it, and run once a minute.

> **The asymmetry was the tell.** The poker sweep (#14) narrows in SQL and carries a comment
> explaining both that and the in-memory `DateTimeOffset` comparison SQLite forces. The retro sweep,
> added later in #23, reached for `GetAllAsync`. This is exactly the kind of drift the shared room
> engine is supposed to prevent, and it slipped through because the two sweeps are separate services
> by design — so the fix is to make the retro one the poker one's mirror image, port included.

> **Left alone on purpose: the once-a-second cadence itself.** Scheduling against the next known
> deadline would remove the idle queries entirely, but it trades one cheap indexed query per second
> for a scheduler with its own failure modes (a missed reschedule is a countdown that never fires).
> Recorded in plan.md §33 as out of scope rather than silently skipped.

## 34. Extract the room-level primitives  `S`  — before any third tool  ✅ done
**Measured the duplication first. Three things were genuinely duplicated; the rest were not.**
- [x] `Core/Countdown.cs` — the deadline primitive: clamp a requested length into the caller's bounds, turn it into one server-authoritative instant. `PokerRoundRules.NormalizeTimerDuration` and `RetroPhaseRules.NormalizeDuration` now delegate, keeping their own bounds.
- [x] `Core/Csv.cs` — RFC-4180 field escaping, plus an invariant date and a row join. Was character-for-character identical in `PokerService` (#12) and `RetroExportRenderer` (#28).
- [x] `Api/RoomSweepService.cs` — the periodic-sweep loop. All **three** background services had their own copy: the round timer (#14), the retro phase countdown (#23) and idle eviction (#37).
- [x] Tests: 13 core (`PrimitiveTests`) + 3 API (`RoomSweepServiceTests`). Backend **662**, coverage gate **95.4% line / 91.7% branch**.

> **I overstated the duplication last time, and measuring corrected it.** I had listed six primitives
> as "tool-private and about to be tripled". Only three were actually duplicated. The other three —
> the hidden-until-reveal projection, the vote budget, and cards-with-grouping — are tool-*private*
> but not tool-*duplicated*: there is exactly one implementation of each, and no second consumer yet.

> **What was deliberately not extracted, and why.**
> - **Vote budget vs estimate stats.** `RetroTallyCalculator` counts n dots across many items;
>   `StatsCalculator` averages one value per person. They look adjacent and share nothing. Merging
>   them would be a shared abstraction over two different domains — the classic mistake.
> - **The expiry *actions*.** Poker force-reveals or auto-advances to a re-vote; a retro only clears
>   the countdown and leaves the phase to the facilitator (#23). That asymmetry is a product
>   decision, and burying it in a shared base class would hide the most interesting line in either
>   tool.
> - **The store queries.** Both sweeps narrow in SQL (#14/#33) but need different includes and
>   different predicates. The shape is a convention worth copying, not code worth sharing.
> - **The phase rail.** Retro's ordered, adjacency-only phase machine is the obvious thing for a
>   third tool to reuse — but poker's Voting/Revealed/Discussion is not a rail, so extracting it now
>   would be generalising from one example. It waits for Lean Coffee (#35), its first real second
>   consumer.

> **The sweep loop's one dangerous behaviour is now tested.** A pass that throws must not end the
> loop — without the catch, one bad tick silently takes a background service down for the life of the
> process and nothing surfaces it. Each service used to carry its own copy of that guarantee; now one
> mistake would break all three, so `RoomSweepService` gets a direct test for it (throw on the first
> pass, assert the third still happens) rather than relying on three services to each be right.
>
> The loop is also covered end to end by the pre-existing hub test that starts a 5-second round timer
> and waits for the background sweep to force-reveal it — which is what proves the extracted base
> class really resolves its scope and broadcasts.

> **`Csv.Date` came out of the extraction, not into it.** Both exports formatted dates
> `yyyy-MM-dd` with `CultureInfo.InvariantCulture` inline. A locale-shaped date in a CSV is how
> 03/04 becomes two different days downstream, so it is now a named function with a test saying so.

---

# Phase 3 — the third and fourth tools

§35 is **built**. §36 is a **spec only** — not a commitment to build it; it exists so the platform's
constraints are confronted on paper first.

| # | Task | Size | Depends on | Area |
| --- | --- | --- | --- | --- |
| 35 | Lean Coffee — the third tool | M | 34 | BE + FE |
| 36 | Async Standup — the fourth | M | 34 | BE + FE |

## 35. Lean Coffee  `M`  — depends on #34  ✅ done
**The third tool, and the test of whether the room engine earned its keep. It did.**
- [x] `RoomTool.Coffee` + `CoffeeBoard` (topics, dot votes, extension votes, decisions, per-topic timings), EF migration ×3 — **purely additive**, no data motion.
- [x] `CoffeeService` + `CoffeePhaseRules`: `Propose → Vote → Discuss → Done`, with the gates per phase.
- [x] **Generalised the retro's phase rail into `PhaseRail<TPhase>`** — the second consumer #34 waited for. Both tools now declare their own phases and gates against it.
- [x] **Generalised dot voting into `DotBudget`** + `IDotVote`: budget from stored rows, the no-stacking rule, the ranking. `RetroTallyCalculator` delegates to it; what stays retro-specific is that a theme's total includes its cards'.
- [x] **Generalised action-item rules into `ActionItemRules`** — title validation and owner resolution, shared by retro actions and coffee decisions.
- [x] Reused unchanged: `Countdown` (#34), `RoomSweepService` (#34 — first tool that didn't write its own loop), `RoomService` (#19), the per-recipient projection (#21), rate limiting, a11y, i18n, retention.
- [x] `CoffeeHub` + `CoffeeTimeboxBackgroundService`; `ICoffeeBoardStore` narrowed in SQL from the start (#33's lesson applied, not rediscovered).
- [x] Frontend: `coffee.client.ts`, create + board pages (lazy-loaded), launcher card, `flattenCoffee`.
- [x] **`core/tool-registry.ts`** — one map from `RoomTool` to a route and a lazily-imported client, replacing the two if/else chains `/join` had grown (#32). A fourth tool is one entry.
- [x] i18n: 84 strings × 4 locales.
- [x] Tests: 77 core (`CoffeeTests` + `CoffeeRoomOperationsTests`) + 4 data + 21 frontend. Backend **743**, frontend **247**, coverage gate **95.1% line / 90.7% branch**.
- [x] Verified end to end in a browser: created a session, proposed a topic, spent a dot, advanced to Discuss with the 5:00 timebox running, and recorded a decision.

> **What was actually new: two things.** A per-topic timebox rather than a per-phase one, and the
> extension vote. Everything else was assembly over primitives that already existed. That is the
> answer to the question #34 posed — if the third tool had cost as much as the second, the shared
> core would not have been paying for itself.

> **Expiry opens the vote; it does not move the room on.** The three tools' countdowns now differ
> deliberately: poker force-reveals (mechanical), a retro does nothing (#23 — facilitation), and a
> Lean Coffee asks the room. Three behaviours over one `Countdown`, which is exactly why #34
> extracted the mechanism and deliberately left the actions alone.

> **Extension answers are hidden until the facilitator closes the vote.** Only the *count* of
> answers goes on the wire while it runs — the same anti-anchoring rule as hidden collection, in the
> same enforcement point (the per-recipient projection). A tie moves on: keep-going has to *win*,
> not merely draw, because the default is to respect the timebox the room agreed to.

> **No anonymity, and no flag for it.** A retro card can be anonymous because criticism is easier
> unsigned; a Lean Coffee topic is an offer to lead a conversation, so the name is the useful part.
> Leaving the flag out entirely is cheaper and clearer than shipping one nobody should turn on.

> **Shared logic, per-tool tables.** `RetroVote` and `CoffeeVote` both implement `IDotVote` and are
> counted by identical code, but they live in their own tables and neither tool references the other
> — the platform's one structural rule. A shared room-level artefact table is the obvious next step
> if a fourth tool lands; it would also be a hand-written migration over shipped retro rows, so it
> was not done speculatively.

> **The branch-coverage gate caught a real gap rather than a formality.** Adding the tool dropped
> Core branch coverage to 86.6%. The uncovered seams were the room-level projections every tool
> service has to write (leave, presence, roles, organisers, password, close, delete) — thin, and
> exactly where a tool can forget to project or project the wrong viewer's board.
> `CoffeeRoomOperationsTests` covers them, as the retro's equivalent does (#21), and the gate is
> back at 90.7% with nothing lowered.

> **Two stale things the new tool exposed.** The home page's heading said "Two tools for your team
> ceremonies" in all four locales, and its spec asserted `toBe(2)` twice. The heading is now
> tool-count-free; the spec counts against the component's own list, so a fourth tool needs one route
> assertion added rather than the numbers edited.

> **Known gap, deliberately left:** the hub exposes `SetTimebox` and `SetVoteBudget`, but the board
> UI has no control for either — they are set at creation. Adding facilitator settings mid-session is
> polish, and worth doing when someone asks for it rather than guessing at the panel.

## 36. Async Standup  `M`  — depends on #34  📋 specced
- [ ] `RoomTool.Standup` + `StandupBoard` (an entry per participant per question), EF migration ×3.
- [ ] **Post-to-read, enforced in the projection** — you see others' answers once you have posted your own. Same principle as hidden collection (#23), same enforcement point as anonymity (#22): the per-recipient snapshot, never the client.
- [ ] **No phase rail.** A standup opens, people post, it closes. This is the tool that shows the rail is a retro/coffee concern, not a platform one — do not reach for #35's extraction just because it exists.
- [ ] Blockers are the only structured field, and can be promoted to an action item with an owner (#26).
- [ ] Start today's room from yesterday's short code, copying the question set and unresolved blockers — #27's carry-over, not a new cross-room concept.
- [ ] i18n ×4; ≥90% Core coverage.

> **This spec exists mostly to write down what the platform will *not* do for it.** A standup wants
> recurrence, notifications and a roster; TeamTools has no scheduler, no addresses and no accounts.
> So: each day is its own room; the invite link *is* the reminder; and the board says "N of the M
> people in this room have posted", never "Dave is missing" — presence only knows who opened it.

> **Retention needs deciding up front, not discovering.** Idle rooms are evicted (#15), and a
> standup room is idle by construction between mornings. The spec's answer is "export it or lose it",
> stated in the UI — matching the platform rather than carving out a special window for one tool.

> **If a constraint here becomes a real problem in use, it is a platform decision, not a per-tool
> workaround.** Accounts, a team entity or a scheduler would change both shipped tools too, and
> should be taken deliberately and once.
