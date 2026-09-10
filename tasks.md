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

## 24. Grouping into themes  `M`  — depends on #23
- [ ] `RetroGroup` + `RetroCard.GroupId`; `RetroHub.GroupCards`/`UngroupCard`/`RenameGroup`.
- [ ] Organiser-driven by default, with a board setting to open grouping to everyone.
- [ ] Concurrent drags: last-write-wins on `GroupId`, reconciled by the full-snapshot rebroadcast.
- [ ] EF migration ×3; snapshot + `core/models.ts`.
- [ ] Drag-and-drop in `retro.page.*` **plus** a keyboard/screen-reader "move card to group" menu on every card (#4 — no mouse-only affordance).
- [ ] Tests: group/ungroup/rename authz; concurrent-move convergence; keyboard move path works without drag events.

## 25. Dot voting  `M`  — depends on #24
- [ ] `RetroVote` (`{ BoardId, VoterUserId, TargetKind, TargetId }`) + `RetroBoard.VoteBudget`/`AllowMultiplePerItem`.
- [ ] `RetroHub.CastRetroVote`/`WithdrawVote` with **server-side** budget enforcement.
- [ ] Tally calculator (a `StatsCalculator` sibling); own dots always visible, others' totals only once Vote ends; Discuss reorders by score.
- [ ] Votes follow their target through grouping: grouping sums dots, ungrouping returns each card's own.
- [ ] EF migration ×3; snapshot + `core/models.ts`.
- [ ] a11y + i18n: dot controls as real buttons with counts announced (#4); budget/remaining copy pluralised via `PluralPipe` (#5).
- [ ] Tests: over-budget rejected server-side; totals absent from the wire during Vote; group/ungroup dot arithmetic; ranking order.

## 26. Action items  `M`  — depends on #23
- [ ] `RetroActionItem` (`{ Id, BoardId, Title, OwnerUserId, OwnerName, DueDate, DoneAt, SourceGroupId?, CarriedFromBoardId? }`).
- [ ] `RetroHub.AddAction`/`EditAction`/`ToggleActionDone`/`DeleteAction`; creatable from a theme (prefilled from the group label) or standalone.
- [ ] Optional free-text owner (no accounts; the owner may not be in the room).
- [ ] Explicit carve-out in the #19 close check so actions stay editable on a **closed** room — "mark done" happens days later.
- [ ] EF migration ×3; snapshot + `core/models.ts`; actions panel in `retro.page.*`.
- [ ] a11y + i18n: due dates rendered via the #5 `Intl` service; panel keyboard-operable (#4).
- [ ] Tests: action CRUD + done toggle; the closed-room write carve-out allows exactly this and nothing else; creation from a group prefills the title.

## 27. Carry-over from the previous retro  `S–M`  — depends on #26
- [ ] Retro creation accepts a previous board's short code; `RetroBoard.PreviousBoardShortCode`.
- [ ] `RetroService.CreateWithCarryOverAsync` **copies** not-done actions (keeping `CarriedFromBoardId`) so the new board stays self-contained and survives the old one's retention delete (#15).
- [ ] Require the previous board's password if it had one — a short code is a bearer token, and carry-over must not leak a protected board.
- [ ] Carried actions shown as a review list at the top of Collect; "start next retro" from a closing board deep-links the code.
- [ ] EF migration ×3 if not already covered by #26's columns.
- [ ] Tests: only not-done actions carry; provenance recorded; wrong/missing password refused; deleting the source board leaves the new one intact.

## 28. Retro export  `S–M`  — depends on #24, #25, #26
- [ ] `GET /api/retro/{shortCode}/export?format=csv|json|md`, reusing #12's streaming, content-type and existence+password guard.
- [ ] Serialise columns, cards, groups, vote tallies and action items; Markdown grouped by theme, ordered by dots, actions as a task list.
- [ ] **Anonymity holds in the export** (#22): an anonymous board exports no author column, with no organiser override.
- [ ] Read-only post-retro summary view + FE download button.
- [ ] Tests: CSV/JSON/MD shapes; password guard; an anonymous board's export contains no author data in any of the three formats.

## 29. `README.md` & deploy  `S`  — depends on #17–#28
**Last: the README describes shipped features, so it waits until they are.**
- [ ] `README.md`: platform intro, per-tool feature lists, updated layout block, run/prerequisite instructions.
- [ ] `ARCHITECTURE.md`: corrections only, where #18–#28 diverged from #17's design — note the divergence rather than quietly rewriting to match the code.
- [ ] `.github/workflows`, `deploy/` and `run.sh`/`run.ps1` help text, for anything the retro tool added that #18 could not settle in advance.
- [ ] One commit, done last, so the docs match a single point in the history. (Docs-only: no test run needed just to source a number.)
