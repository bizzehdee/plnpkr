using PlanningPoker.Core;
using PlanningPoker.Core.Models;

namespace PlanningPoker.Core.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ISessionStore"/> for fast SessionService behaviour tests. Holds sessions by
/// reference (mirroring EF change tracking) and assigns participant ids so snapshot ordering is
/// deterministic. Can be told to throw <see cref="DuplicateNameException"/> to simulate the DB
/// unique-constraint race.
/// </summary>
public sealed class FakeSessionStore : ISessionStore
{
    private readonly Dictionary<Guid, Session> _byId = new();
    private int _nextParticipantId = 1;

    /// <summary>When set, the next <see cref="UpdateAsync"/> throws to simulate a concurrent name clash.</summary>
    public bool ThrowDuplicateOnNextUpdate { get; set; }

    // Mirror EF's global query filter: soft-deleted sessions (DeletedAt set) are invisible to reads (#26).
    public Task<Session?> FindByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var session = _byId.Values.FirstOrDefault(s =>
            s.DeletedAt == null && string.Equals(s.ShortCode, shortCode, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(session);
    }

    public Task<Session?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = _byId.TryGetValue(id, out var found) && found.DeletedAt == null ? found : null;
        return Task.FromResult(session);
    }

    public Task<bool> ShortCodeExistsAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var exists = _byId.Values.Any(s =>
            s.DeletedAt == null && string.Equals(s.ShortCode, shortCode, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }

    public Task AddAsync(Session session, CancellationToken cancellationToken = default)
    {
        AssignParticipantIds(session);
        _byId[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Session session, CancellationToken cancellationToken = default)
    {
        if (ThrowDuplicateOnNextUpdate)
        {
            ThrowDuplicateOnNextUpdate = false;
            throw new DuplicateNameException("Simulated unique-constraint violation.");
        }

        AssignParticipantIds(session);
        _byId[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Session session, CancellationToken cancellationToken = default)
    {
        _byId.Remove(session.Id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Session>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Session>>(_byId.Values.Where(s => s.DeletedAt == null).ToList());

    // Mirror the EF query: only Voting, not-deleted sessions whose deadline is at/before asOf.
    public Task<IReadOnlyList<Session>> GetSessionsWithExpiredTimerAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Session>>(_byId.Values
            .Where(s => s.DeletedAt == null
                && s.State == SessionState.Voting
                && s.TimerDeadline is { } deadline && deadline <= asOf)
            .ToList());

    // Mirror the EF projected check: exists, not soft-deleted, reactions on.
    public Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.Values.Any(s =>
            s.DeletedAt == null
            && s.ReactionsEnabled
            && string.Equals(s.ShortCode, shortCode, StringComparison.OrdinalIgnoreCase)));

    private void AssignParticipantIds(Session session)
    {
        foreach (var p in session.Participants.Where(p => p.Id == 0))
        {
            p.Id = _nextParticipantId++;
        }
    }
}
