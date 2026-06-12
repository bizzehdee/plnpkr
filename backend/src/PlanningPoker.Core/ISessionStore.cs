using PlanningPoker.Core.Models;

namespace PlanningPoker.Core;

/// <summary>
/// Persistence abstraction for sessions. Deliberately leaks no EF Core types so domain logic in
/// <see cref="SessionService"/> can be tested against an in-memory fake.
/// The returned <see cref="Session"/> is expected to include its <see cref="Session.Participants"/>.
/// </summary>
public interface ISessionStore
{
    /// <summary>Loads a session (with participants) by its invite short code, or null if not found.</summary>
    Task<Session?> FindByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);

    /// <summary>Loads a session (with participants) by id, or null if not found.</summary>
    Task<Session?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True if a session already uses the given short code.</summary>
    Task<bool> ShortCodeExistsAsync(string shortCode, CancellationToken cancellationToken = default);

    /// <summary>Persists a brand-new session.</summary>
    Task AddAsync(Session session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to an existing session aggregate (e.g. an added/removed/modified participant).
    /// Implementations must translate a unique-name constraint violation into
    /// <see cref="DuplicateNameException"/> so the race is handled as a domain outcome.
    /// </summary>
    Task UpdateAsync(Session session, CancellationToken cancellationToken = default);

    /// <summary>Deletes a session and its participants. Used by idle eviction. See #37.</summary>
    Task RemoveAsync(Session session, CancellationToken cancellationToken = default);

    /// <summary>All sessions (with participants). Used by idle eviction; fine at single-host scale.</summary>
    Task<IReadOnlyList<Session>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads only the sessions (with participants) that are still <see cref="SessionState.Voting"/> and
    /// whose running round-timer deadline is at or before <paramref name="asOf"/>. Filtered in the data
    /// store so the once-per-second expiry pass doesn't materialise the whole table. See #14.
    /// </summary>
    Task<IReadOnlyList<Session>> GetSessionsWithExpiredTimerAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a session exists (and is not soft-deleted) with reactions enabled. A projected existence
    /// check — it must not load the session aggregate, as it runs on the per-reaction hot path. See #17.
    /// </summary>
    Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown when a participant display name collides within a session — either detected up front or
/// surfaced by the database unique constraint under a concurrent join. See #7.
/// </summary>
public sealed class DuplicateNameException : Exception
{
    public DuplicateNameException(string message) : base(message) { }
}
