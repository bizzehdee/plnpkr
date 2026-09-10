using TeamTools.Core.Models;

namespace TeamTools.Core.Coffee;

/// <summary>
/// Timebox expiry for Lean Coffee (#35) — the third sibling of <c>PokerTimerService</c> and
/// <c>RetroPhaseTimerService</c>.
/// <para>
/// <b>Expiry opens the extension vote; it does not move the room on.</b> A timebox running out is
/// the prompt to ask "keep going or next?", which is the defining moment of a Lean Coffee — so the
/// server opens that vote and stops there. Poker can auto-reveal because a reveal is mechanical; a
/// retro deliberately does nothing at all (#23); this sits between them, and the difference is the
/// product, not an inconsistency.
/// </para>
/// </summary>
public class CoffeeTimerService
{
    private readonly ICoffeeBoardStore _store;
    private readonly IRoomStore _rooms;
    private readonly CoffeeService _coffee;
    private readonly IClock _clock;

    public CoffeeTimerService(
        ICoffeeBoardStore store, IRoomStore rooms, CoffeeService coffee, IClock clock)
    {
        _store = store;
        _rooms = rooms;
        _coffee = coffee;
        _clock = clock;
    }

    /// <summary>
    /// Opens the extension vote on any board whose timebox has just run out, and returns the
    /// affected rooms so the caller can re-broadcast (per recipient, as coffee snapshots always
    /// are).
    /// </summary>
    public async Task<IReadOnlyList<Room>> ExpireDueTimeboxesAsync(CancellationToken ct = default)
    {
        var expired = new List<Room>();

        foreach (var room in await _store.GetRoomsWithExpiredTimeboxAsync(_clock.UtcNow, ct))
        {
            if (room.CoffeeBoard is not { } board || board.CurrentTopicId is null)
            {
                continue;
            }

            // Accumulate the stretch that just ended, then ask the room. The deadline is cleared so
            // the sweep does not fire again on the next tick while the vote is open.
            _coffee.AccumulateElapsed(board);
            board.PhaseDeadline = null;
            board.ExtendVoteOpen = true;
            board.ExtendVotes.Clear();
            room.LastActivityAt = _clock.UtcNow;

            await _rooms.UpdateAsync(room, ct);
            expired.Add(room);
        }

        return expired;
    }
}
