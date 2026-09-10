using TeamTools.Core.Models;

namespace TeamTools.Core.Coffee;

/// <summary>
/// The one persistence query specific to Lean Coffee — the third sibling of
/// <see cref="Poker.IPokerRoundStore"/> and <see cref="Retro.IRetroBoardStore"/> (#19/#33/#35). Kept
/// off <see cref="IRoomStore"/> so the room engine carries no knowledge of timeboxes; the EF adapter
/// implements all four ports.
/// </summary>
public interface ICoffeeBoardStore
{
    /// <summary>
    /// Loads only the coffee rooms whose timebox has expired, with their board and topics.
    /// <para>
    /// Narrowed in SQL because the expiry pass runs once per second forever — the lesson #33 learned
    /// the expensive way, applied here from the start rather than discovered later. Topics are
    /// included because expiry accumulates the time spent on the current one.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Room>> GetRoomsWithExpiredTimeboxAsync(
        DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
