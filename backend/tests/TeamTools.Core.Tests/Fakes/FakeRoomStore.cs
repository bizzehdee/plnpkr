using TeamTools.Core;
using TeamTools.Core.Models;
using TeamTools.Core.Poker;

namespace TeamTools.Core.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IRoomStore"/> for fast PokerService behaviour tests. Holds rooms by
/// reference (mirroring EF change tracking) and assigns participant ids so snapshot ordering is
/// deterministic. Can be told to throw <see cref="DuplicateNameException"/> to simulate the DB
/// unique-constraint race.
/// </summary>
public sealed class FakeRoomStore : IRoomStore, IPokerRoundStore
{
    private readonly Dictionary<Guid, Room> _byId = new();
    private int _nextParticipantId = 1;

    /// <summary>When set, the next <see cref="UpdateAsync"/> throws to simulate a concurrent name clash.</summary>
    public bool ThrowDuplicateOnNextUpdate { get; set; }

    // Mirror EF's global query filter: soft-deleted rooms (DeletedAt set) are invisible to reads (#26).
    public Task<Room?> FindByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var room = _byId.Values.FirstOrDefault(s =>
            s.DeletedAt == null && string.Equals(s.ShortCode, shortCode, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(room);
    }

    public Task<Room?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var room = _byId.TryGetValue(id, out var found) && found.DeletedAt == null ? found : null;
        return Task.FromResult(room);
    }

    public Task<bool> ShortCodeExistsAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var exists = _byId.Values.Any(s =>
            s.DeletedAt == null && string.Equals(s.ShortCode, shortCode, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }

    public Task AddAsync(Room room, CancellationToken cancellationToken = default)
    {
        AssignParticipantIds(room);
        _byId[room.Id] = room;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Room room, CancellationToken cancellationToken = default)
    {
        if (ThrowDuplicateOnNextUpdate)
        {
            ThrowDuplicateOnNextUpdate = false;
            throw new DuplicateNameException("Simulated unique-constraint violation.");
        }

        AssignParticipantIds(room);
        _byId[room.Id] = room;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Room room, CancellationToken cancellationToken = default)
    {
        _byId.Remove(room.Id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Room>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Room>>(_byId.Values.Where(s => s.DeletedAt == null).ToList());

    // Mirror the EF query: only poker rooms mid-countdown, not deleted, deadline at/before asOf.
    public Task<IReadOnlyList<Room>> GetRoomsWithExpiredTimerAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Room>>(_byId.Values
            .Where(r => r.DeletedAt == null
                && r.PokerRound is { } round
                && (round.State == SessionState.Voting || round.State == SessionState.Discussion)
                && round.TimerDeadline is { } deadline && deadline <= asOf)
            .ToList());

    // Mirror the EF projected check: exists, not soft-deleted, reactions on.
    public Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.Values.Any(s =>
            s.DeletedAt == null
            && s.ReactionsEnabled
            && string.Equals(s.ShortCode, shortCode, StringComparison.OrdinalIgnoreCase)));

    // Mirror the EF IgnoreQueryFilters() query: soft-deleted rooms past the retention threshold (#15).
    public Task<IReadOnlyList<Room>> GetSoftDeletedPastRetentionAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Room>>(_byId.Values
            .Where(s => s.DeletedAt is { } deletedAt && deletedAt <= threshold)
            .ToList());

    private void AssignParticipantIds(Room room)
    {
        foreach (var p in room.Participants.Where(p => p.Id == 0))
        {
            p.Id = _nextParticipantId++;
        }
    }
}
