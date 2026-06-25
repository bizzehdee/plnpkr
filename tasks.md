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
- [ ] Confirm `Participant.HasVoted` is in the participant snapshot (`Core/Contracts/Snapshots.cs`) during Voting — value never included.
- [ ] Render per-seat "voted / waiting" badge in `pages/session/session.page.html`.
- [ ] Mirror any snapshot field in `core/models.ts`.
- [ ] Tests: snapshot omits vote value pre-reveal; FE renders badge.

## 2. Sound / visual cue on reveal  `S`
- [ ] Add `core/sound.service.ts` + a small audio asset in `frontend/public`.
- [ ] Detect `Voting → Revealed` transition in `session.page.ts`; play cue + reveal animation.
- [ ] Per-browser mute toggle in `localStorage`; respect `prefers-reduced-motion`.
- [ ] Tests: transition triggers cue; mute suppresses it.

## 3. Rate limiting / abuse protection  `S–M`
- [ ] Token-bucket per connection/IP for `CreateSession`/`JoinSession` in `Hubs/PlanningPokerHub.cs` (reuse `Hubs/ReactionRateLimiter.cs` pattern).
- [ ] Max-participants guard in `SessionService`; friendly `SessionActionResult` failures.
- [ ] ASP.NET Core rate-limiting middleware for REST endpoints in `Program.cs`.
- [ ] Limits configurable in `appsettings.json`.
- [ ] Tests: exceeding the bucket is rejected; under-limit passes.

## 4. Accessibility baseline  `M`  — depends on #1
- [ ] Cards → buttons in a roving-tabindex radiogroup; full keyboard voting.
- [ ] `aria-live` region announcing results/vote-status on reveal (uses #1).
- [ ] Focus trap + restore in all modals; visible focus rings.
- [ ] Color-contrast audit in light + dark themes.
- [ ] Tests: keyboard cast vote; aria attributes present.

## 5. i18n / localization  `M`
**Do before later UI features so their strings are translatable.**
- [ ] Choose + wire i18n library (runtime-switchable preferred); add `en` base catalog.
- [ ] Migrate existing `home`/`join`/`session` templates to translation keys.
- [ ] Language switcher persisted in `localStorage`; locale-aware number/plural formatting.
- [ ] Map backend tracker/error messages to client keys.
- [ ] Tests: switching locale swaps strings.

## 6. Spectator count / large-group mode  `M`  — depends on #1
- [ ] Summary header: "N voted of M", observer/spectator count badge.
- [ ] Virtualized/collapsed participant list above a threshold.
- [ ] Verify `SessionUpdated` payload size for large rooms; trim snapshot in `SessionService` if needed.
- [ ] Tests: summary counts correct; large list virtualizes.

## 7. Facilitator hand-off / multiple organisers  `M`  — needed by #9
- [ ] Model: single `Session.OrganiserUserId` → multiple (set, or `Participant.IsOrganiser`). EF migration (all 3 providers).
- [ ] Replace single-organiser authz in `SessionService` with set membership.
- [ ] Hub methods: `PromoteToOrganiser`, `DemoteOrganiser`, `TransferOrganiser`.
- [ ] Auto-succession when all organisers disconnect (longest-connected wins).
- [ ] Snapshot + FE controls; mirror in `core/models.ts`.
- [ ] Tests: promote/demote/transfer authz; succession.

## 8. Discussion / re-vote prompt on disagreement  `M`  — depends on #1
- [ ] On `Revealed` with `!consensus`, render discussion banner highlighting outlier seats (outliers already in reveal snapshot via `StatsCalculator`).
- [ ] "Discuss & re-vote" action reusing existing `ResetRound`.
- [ ] Tests: banner shows only on disagreement; action resets round.

## 9. Timed discussion phase  `M–L`  — depends on #7, #8
- [ ] Add `SessionState.Discussion` (`Core/Models/Enums.cs`) + transitions `Revealed → Discussion → Voting` in `SessionService`.
- [ ] Hub methods `StartDiscussion`/`EndDiscussion`, organiser-gated (respects #7).
- [ ] Reuse `RoundTimerService` deadline broadcast for the discussion countdown.
- [ ] Entry can be auto-triggered by the #8 prompt.
- [ ] Snapshot + FE state; mirror in `core/models.ts`. EF migration if state persisted.
- [ ] Tests: state machine transitions; timer expiry behavior.

## 10. Notes / comments per story  `M`  — foundation for #11, #12
- [ ] Persisted per-round note field/entity; EF migration (all 3 providers).
- [ ] Hub method `SetStoryNote`; configurable who may edit (organiser vs anyone).
- [ ] Broadcast in snapshot; FE editor in `session.page.html`; mirror in `core/models.ts`.
- [ ] Lay groundwork for a round-history record (#11 extends it).
- [ ] Tests: note persists + broadcasts; edit authz.

## 11. Velocity / throughput analytics  `L`  — depends on #10
- [ ] New `RoundResult` entity `{ story, finalEstimate, stats, startedAt, endedAt }` + EF migration (all 3 providers).
- [ ] Capture "agreed estimate" on round completion; persist round record in `SessionService`.
- [ ] Server-side aggregates: items estimated, totals, time-per-story, consensus rate.
- [ ] Expose via `SessionsController` endpoint or snapshot section; FE summary view.
- [ ] Tests: round recorded on completion; aggregates correct.

## 12. Export  `M`  — depends on #10, #11
- [ ] `GET /api/sessions/{shortCode}/export?format=csv|json` on `SessionsController`.
- [ ] Serialize round history (#11) + notes (#10) + final estimates; correct content-type/streaming.
- [ ] Guard with session existence (+ password if set).
- [ ] FE download button + post-session summary view.
- [ ] Tests: CSV + JSON shape; auth guard.

## 13. GitHub Issues support  `L`
**Establishes multi-provider patterns reused by #14.**
- [ ] `IntegrationProvider.GitHub` in `Core/Integrations/IssueTracking.cs`.
- [ ] `PlanningPoker.Integrations/GitHubIssueTracker.cs` implementing `IIssueTracker` (Validate/GetIssue/SetStoryPoints/Search).
- [ ] Map story points → label or Projects v2 field (configurable).
- [ ] Register in `IssueTrackerFactory`; OAuth/PAT config; host allowlist (`TrackerHostPolicy`); feature flag in `appsettings.json`.
- [ ] Tests: adapter ops against a fake HTTP handler; factory resolution.

## 14. GitLab support  `L`  — depends on #13
- [ ] `IntegrationProvider.GitLab`; `GitLabIssueTracker.cs` implementing `IIssueTracker`.
- [ ] Map story points → native issue **weight** (label fallback).
- [ ] Register in factory + host policy + feature flag; reuse #13 OAuth/config patterns.
- [ ] Queue from project/group/search.
- [ ] Tests: adapter ops; factory resolution.
