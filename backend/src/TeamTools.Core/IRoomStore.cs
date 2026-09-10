using TeamTools.Core.Models;

namespace TeamTools.Core;

/// <summary>
/// Persistence abstraction for rooms. Deliberately leaks no EF Core types so domain logic in
/// <see cref="RoomService"/> and the per-tool services can be tested against an in-memory fake.
/// The returned <see cref="Room"/> is expected to include its <see cref="Room.Participants"/> and
/// its tool payload (<see cref="Room.PokerRound"/> with its round history).
/// </summary>
public interface IRoomStore
{
    /// <summary>Loads a room (with participants) by its invite short code, or null if not found.</summary>
    Task<Room?> FindByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);

    /// <summary>Loads a room (with participants) by id, or null if not found.</summary>
    Task<Room?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True if a room already uses the given short code.</summary>
    Task<bool> ShortCodeExistsAsync(string shortCode, CancellationToken cancellationToken = default);

    /// <summary>Persists a brand-new room.</summary>
    Task AddAsync(Room room, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to an existing room aggregate (e.g. an added/removed/modified participant).
    /// Implementations must translate a unique-name constraint violation into
    /// <see cref="DuplicateNameException"/> so the race is handled as a domain outcome.
    /// </summary>
    Task UpdateAsync(Room room, CancellationToken cancellationToken = default);

    /// <summary>Deletes a room and its participants. Used by idle eviction. See #37.</summary>
    Task RemoveAsync(Room room, CancellationToken cancellationToken = default);

    /// <summary>All rooms (with participants). Used by idle eviction; fine at single-host scale.</summary>
    Task<IReadOnlyList<Room>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a room exists (and is not soft-deleted) with reactions enabled. A projected existence
    /// check — it must not load the room aggregate, as it runs on the per-reaction hot path. See #17.
    /// </summary>
    Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deleted rooms (with participants) whose <c>DeletedAt</c> is at or before
    /// <paramref name="threshold"/> — candidates for hard deletion under the retention policy (#15).
    /// Soft-deleted rooms sit behind the normal "not deleted" query filter, so implementations must
    /// bypass it here (EF: <c>IgnoreQueryFilters()</c>) to find them.
    /// </summary>
    Task<IReadOnlyList<Room>> GetSoftDeletedPastRetentionAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown when a participant display name collides within a room — either detected up front or
/// surfaced by the database unique constraint under a concurrent join. See #7.
/// </summary>
public sealed class DuplicateNameException : Exception
{
    public DuplicateNameException(string message) : base(message) { }
}
