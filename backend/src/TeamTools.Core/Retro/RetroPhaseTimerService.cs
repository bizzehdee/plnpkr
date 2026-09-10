using TeamTools.Core.Models;

namespace TeamTools.Core.Retro;

/// <summary>
/// Server-authoritative phase-countdown expiry (#23) — the retro counterpart of
/// <c>PokerTimerService</c>, reusing the same deadline pattern: one instant every client ticks
/// against, and a server sweep that decides when it has passed.
/// <para>
/// Expiry **clears the countdown; it does not advance the phase.** A retro is facilitated, and a
/// timer running out is a prompt for the person running it, not a reason to move a room full of
/// people on mid-sentence. Poker can auto-reveal because a reveal is mechanical; "we are done
/// grouping" is a judgement call.
/// </para>
/// </summary>
public class RetroPhaseTimerService
{
    private readonly IRoomStore _rooms;
    private readonly IClock _clock;

    public RetroPhaseTimerService(IRoomStore rooms, IClock clock)
    {
        _rooms = rooms;
        _clock = clock;
    }

    /// <summary>
    /// Clears any elapsed phase countdown and returns the affected rooms so the caller can
    /// re-broadcast (per recipient, as retro snapshots always are).
    /// </summary>
    public async Task<IReadOnlyList<Room>> ExpireDuePhaseTimersAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var expired = new List<Room>();

        foreach (var room in await _rooms.GetAllAsync(ct))
        {
            if (room.RetroBoard is not { PhaseDeadline: { } deadline } board || deadline > now)
            {
                continue;
            }

            board.PhaseDeadline = null;
            room.LastActivityAt = now;
            await _rooms.UpdateAsync(room, ct);
            expired.Add(room);
        }

        return expired;
    }
}
