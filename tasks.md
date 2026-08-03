# plnpkr — Task Queue

Tasks derived from [plan.md](./plan.md), ordered for execution. Ordering rule:
**ascending ease of implementation, but a task that others depend on always comes
before its dependents** (dependency wins over pure ease). Effort is a rough T-shirt size.

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

## 15. Session retention policy  `S–M`
**Builds on the `ClosedAt`/`DeletedAt` fields from #26; no new persistence.**
- [ ] `ISessionStore.GetSoftDeletedPastRetentionAsync` (EF: `IgnoreQueryFilters()` past the `DeletedAt` global filter) + in-memory test store equivalent.
- [ ] Replace the empty/idle-60-min hard-delete branch in `SessionMaintenanceService.PurgeAsync` with: soft-deleted 30d → hard delete; closed 12mo (not yet soft-deleted) → soft delete; neither, idle 30d → soft delete.
- [ ] `SessionEvictionService`: broadcast `SessionUpdated` on soft-delete transitions, keep existing `SessionClosed` on hard delete.
- [ ] Retention windows configurable in `appsettings.json`.
- [ ] Expose the configured windows to the frontend (new lightweight config read, or fold into an existing config surface — no dedicated endpoint exists yet).
- [ ] Show the windows in the "Close or delete session" modal (`session.page.html:719-750`): next to **Close**, note it auto-deletes N months after closing; next to **Delete**, note it's permanently removed N days after deletion.
- [ ] Tests: all three transition rules; just-under-threshold untouched; hard delete removes `RoundResult`s; old 60-min/empty-room immediate delete no longer fires; modal renders the configured windows.
