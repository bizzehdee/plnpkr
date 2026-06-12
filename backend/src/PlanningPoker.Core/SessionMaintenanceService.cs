using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;

namespace PlanningPoker.Core;

/// <summary>
/// Idle eviction: removes participants who have been disconnected past a grace period and deletes
/// sessions that are empty or have had no activity for a while. Clock-driven so it is fully
/// unit-testable. See #37.
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
    /// <param name="sessionIdle">How long a session may have no activity before deletion.</param>
    public async Task<PurgeReport> PurgeAsync(TimeSpan disconnectGrace, TimeSpan sessionIdle, CancellationToken ct = default)
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

            var idle = session.LastActivityAt + sessionIdle <= now;
            if (session.Participants.Count == 0 || idle)
            {
                await _store.RemoveAsync(session, ct);
                removedShortCodes.Add(session.ShortCode);
            }
            else if (stale.Count > 0)
            {
                await _store.UpdateAsync(session, ct);
                updatedSessions.Add(SessionService.ToSnapshot(session));
            }
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
            // Timebox is up: force the reveal regardless of who has voted, and clear the timer so it
            // fires exactly once. See #14.
            session.State = SessionState.Revealed;
            session.TimerDeadline = null;
            session.TimerPausedRemainingSeconds = null;
            session.LastActivityAt = now;

            await _store.UpdateAsync(session, ct);
            revealed.Add(SessionService.ToSnapshot(session));
        }

        return revealed;
    }
}

/// <summary>Outcome of a purge: which sessions were deleted and which had participants removed.</summary>
public record PurgeReport(
    IReadOnlyList<string> RemovedShortCodes,
    IReadOnlyList<SessionSnapshot> UpdatedSessions);
