using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;

namespace PlanningPoker.Core;

/// <summary>
/// Idle eviction + long-term retention (#15): removes participants who have been disconnected past a
/// grace period, and applies the retention policy layered on <see cref="Session.ClosedAt"/>/
/// <see cref="Session.DeletedAt"/> (#26) — a closed session is soft-deleted <see
/// cref="RetentionOptions.ClosedRetentionMonths"/> months after closing; a session that's neither
/// closed nor already soft-deleted is soft-deleted after <see
/// cref="RetentionOptions.IdleRetentionDays"/> days of no activity; a soft-deleted session is
/// permanently (hard) deleted <see cref="RetentionOptions.SoftDeleteRetentionDays"/> days later.
/// Clock-driven so it is fully unit-testable. See #37.
/// </summary>
public class SessionMaintenanceService
{
    private readonly ISessionStore _store;
    private readonly IClock _clock;

    public SessionMaintenanceService(ISessionStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    /// <param name="disconnectGrace">How long a disconnected participant is kept for reconnect.</param>
    /// <param name="retention">The soft/hard-delete windows for the retention policy (#15).</param>
    public async Task<PurgeReport> PurgeAsync(TimeSpan disconnectGrace, RetentionOptions retention, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var removedShortCodes = new List<string>();
        var updatedSessions = new List<SessionSnapshot>();

        foreach (var session in await _store.GetAllAsync(ct))
        {
            var stale = session.Participants
                .Where(p => !p.IsConnected && p.LastSeenAt + disconnectGrace <= now)
                .ToList();

            foreach (var p in stale)
            {
                session.Participants.Remove(p);
                if (session.OrganiserUserId == p.UserId)
                {
                    // The organiser was evicted while away → fall back to no-organiser. See #39.
                    session.OrganiserUserId = null;
                }
            }

            // Retention (#15): a closed session auto soft-deletes after ClosedRetentionMonths; a session
            // that's neither closed nor already soft-deleted auto soft-deletes after IdleRetentionDays of
            // no activity. `AddMonths` (not a fixed day count) so "12 months" tracks calendar months.
            var shouldSoftDelete =
                (session.ClosedAt is { } closedAt && closedAt.AddMonths(retention.ClosedRetentionMonths) <= now)
                || (session.ClosedAt is null && session.LastActivityAt.AddDays(retention.IdleRetentionDays) <= now);

            if (shouldSoftDelete)
            {
                // Same effect as an organiser-triggered DeleteSessionAsync: gone from every read behind
                // the global query filter, so connected clients are told it's closed, not sent a snapshot.
                session.DeletedAt = now;
                await _store.UpdateAsync(session, ct);
                removedShortCodes.Add(session.ShortCode);
            }
            else if (stale.Count > 0)
            {
                await _store.UpdateAsync(session, ct);
                updatedSessions.Add(SessionService.ToSnapshot(session));
            }
        }

        // Soft-deleted sessions past SoftDeleteRetentionDays are hard-deleted (row + RoundResults gone).
        var hardDeleteThreshold = now.AddDays(-retention.SoftDeleteRetentionDays);
        foreach (var session in await _store.GetSoftDeletedPastRetentionAsync(hardDeleteThreshold, ct))
        {
            await _store.RemoveAsync(session, ct);
            removedShortCodes.Add(session.ShortCode);
        }

        return new PurgeReport(removedShortCodes, updatedSessions);
    }

    /// <summary>
    /// Force-reveals any session whose running round-timer deadline has passed (server-authoritative
    /// expiry, #14). Returns the post-reveal snapshots so the caller can broadcast them. Runs on a
    /// tight cadence (separate from the idle purge) so the reveal fires promptly when time's up.
    /// </summary>
    public async Task<IReadOnlyList<SessionSnapshot>> ExpireDueRoundTimersAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var revealed = new List<SessionSnapshot>();

        // The store returns only Voting sessions whose deadline has passed — no full-table scan per tick.
        foreach (var session in await _store.GetSessionsWithExpiredTimerAsync(now, ct))
        {
            // Timebox is up. A voting round force-reveals regardless of who has voted (#14); a
            // discussion phase auto-advances to a fresh re-vote, clearing votes (#9). Either way the
            // timer is cleared so it fires exactly once.
            if (session.State == SessionState.Discussion)
            {
                foreach (var p in session.Participants)
                {
                    p.Vote = null;
                    p.HasVoted = false;
                    p.ChangedAfterReveal = false;
                }

                session.State = SessionState.Voting;
            }
            else
            {
                session.State = SessionState.Revealed;
            }

            session.TimerDeadline = null;
            session.TimerPausedRemainingSeconds = null;
            session.LastActivityAt = now;

            await _store.UpdateAsync(session, ct);
            revealed.Add(SessionService.ToSnapshot(session));
        }

        return revealed;
    }
}

/// <summary>
/// Outcome of a purge. <see cref="RemovedShortCodes"/> covers both hard-deleted sessions and sessions
/// newly soft-deleted this pass — to a connected client both mean "gone" (broadcast SessionClosed for
/// each). <see cref="UpdatedSessions"/> is still-alive sessions whose participant list changed only
/// (broadcast the new snapshot).
/// </summary>
public record PurgeReport(
    IReadOnlyList<string> RemovedShortCodes,
    IReadOnlyList<SessionSnapshot> UpdatedSessions);
