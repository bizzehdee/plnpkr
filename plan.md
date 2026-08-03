# plnpkr — Feature Plan

Planned enhancements to the Planning Poker app. Each feature below lists **what** it
delivers, **why**, the **touch points** in the current codebase, and a sketched
**approach**. Implementation order and dependencies live in [tasks.md](./tasks.md).

## Architecture reference (current)

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

## Cross-cutting notes

- **Tests.** Backend has a ≥90% Core coverage gate (xUnit); every Core change needs unit
  tests against the in-memory store + fake `IClock`. Frontend uses Vitest + TestBed.
- **Migrations.** Any `Session`/`Participant`/new-entity change needs EF migrations for
  all three providers (Sqlite, SqlServer, PostgreSql).
- **Snapshot contract.** New broadcast fields go through `Snapshots.cs` and the mirrored
  `core/models.ts` — keep them in sync.
- **Scaling caveat.** The app is single-instance (in-process SignalR + SQLite). Analytics
  history and large-group broadcasts are fine at that scale; horizontal scaling (Redis
  backplane) is out of scope here.
