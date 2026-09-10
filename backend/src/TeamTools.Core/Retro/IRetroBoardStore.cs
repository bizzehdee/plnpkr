using TeamTools.Core.Models;

namespace TeamTools.Core.Retro;

/// <summary>
/// The one persistence query that is specific to Team Retro rather than to rooms in general — the
/// retro sibling of <see cref="Poker.IPokerRoundStore"/> (#19). Kept off <see cref="IRoomStore"/> so
/// the room engine carries no knowledge of phase countdowns; the EF adapter implements all three.
/// </summary>
public interface IRetroBoardStore
{
    /// <summary>
    /// Loads only the retro rooms whose phase countdown deadline is at or before
    /// <paramref name="asOf"/>, with their board and nothing else.
    /// <para>
    /// Filtered in the data store because the expiry pass runs <b>once per second, forever</b>. It
    /// used to go through <c>GetAllAsync</c>, which loads every room in the database together with
    /// its participants, round history and the whole retro board graph — a nine-way join whose cost
    /// grew with every room ever created, whether or not any countdown was running (#33).
    /// </para>
    /// <para>
    /// The board alone is enough: the caller clears the deadline and saves, then re-reads each board
    /// per recipient to broadcast it, because a retro snapshot is projected per viewer (#21).
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Room>> GetRoomsWithExpiredPhaseAsync(
        DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
