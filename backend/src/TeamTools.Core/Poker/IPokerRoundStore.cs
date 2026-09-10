using TeamTools.Core.Models;

namespace TeamTools.Core.Poker;

/// <summary>
/// The one persistence query that is specific to Planning Poker rather than to rooms in general
/// (#19). Kept off <see cref="IRoomStore"/> so the room engine carries no knowledge of round timers;
/// the EF adapter implements both interfaces.
/// </summary>
public interface IPokerRoundStore
{
    /// <summary>
    /// Loads only the poker rooms (with participants and round) whose round is still
    /// <see cref="SessionState.Voting"/> and whose running timer deadline is at or before
    /// <paramref name="asOf"/>. Filtered in the data store so the once-per-second expiry pass doesn't
    /// materialise the whole table. See #14.
    /// </summary>
    Task<IReadOnlyList<Room>> GetRoomsWithExpiredTimerAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
