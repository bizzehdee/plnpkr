using TeamTools.Core.Contracts;
using TeamTools.Core.Models;

namespace TeamTools.Core.Poker;

/// <summary>
/// Server-authoritative round-timer expiry (#14, #9). Split out of the old combined maintenance
/// service (#19) because it is poker-specific: the room engine's purge/retention pass knows nothing
/// about rounds, and the retro phase countdown will get its own equivalent.
/// </summary>
public class PokerTimerService
{
    private readonly IPokerRoundStore _store;
    private readonly IRoomStore _rooms;
    private readonly IClock _clock;

    public PokerTimerService(IPokerRoundStore store, IRoomStore rooms, IClock clock)
    {
        _store = store;
        _rooms = rooms;
        _clock = clock;
    }

    /// <summary>
    /// Force-reveals any session whose running round-timer deadline has passed. Returns the
    /// post-reveal snapshots so the caller can broadcast them. Runs on a tight cadence (separate from
    /// the idle purge) so the reveal fires promptly when time's up.
    /// </summary>
    public async Task<IReadOnlyList<SessionSnapshot>> ExpireDueRoundTimersAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var revealed = new List<SessionSnapshot>();

        // The store returns only Voting rounds whose deadline has passed — no full-table scan per tick.
        foreach (var room in await _store.GetRoomsWithExpiredTimerAsync(now, ct))
        {
            if (room.PokerRound is not { } round)
            {
                continue;
            }

            // Timebox is up. A voting round force-reveals regardless of who has voted (#14); a
            // discussion phase auto-advances to a fresh re-vote, clearing votes (#9). Either way the
            // timer is cleared so it fires exactly once.
            if (round.State == SessionState.Discussion)
            {
                foreach (var p in room.Participants)
                {
                    PokerRoundRules.ClearVote(p);
                }

                round.State = SessionState.Voting;
            }
            else
            {
                round.State = SessionState.Revealed;
            }

            PokerRoundRules.StopRunningTimer(round);
            room.LastActivityAt = now;

            await _rooms.UpdateAsync(room, ct);
            revealed.Add(PokerService.ToSnapshot(room));
        }

        return revealed;
    }
}
