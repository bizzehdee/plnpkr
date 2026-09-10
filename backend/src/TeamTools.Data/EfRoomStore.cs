using Microsoft.EntityFrameworkCore;
using TeamTools.Core;
using TeamTools.Core.Models;
using TeamTools.Core.Poker;
using TeamTools.Core.Coffee;
using TeamTools.Core.Retro;

namespace TeamTools.Data;

/// <summary>
/// EF Core implementation of <see cref="IRoomStore"/>, <see cref="IPokerRoundStore"/> and
/// <see cref="IRetroBoardStore"/>. Loads the room aggregate with its participants and tool payload
/// (tracked, so collection edits persist on save) and translates the unique-name constraint
/// violation into <see cref="DuplicateNameException"/>.
/// <para>
/// One adapter implements all three ports (#19): the room engine and each tool have separate
/// persistence contracts, but they share a <see cref="DbContext"/> and therefore a unit of work.
/// </para>
/// </summary>
public class EfRoomStore : IRoomStore, IPokerRoundStore, IRetroBoardStore, ICoffeeBoardStore
{
    private readonly TeamToolsDbContext _db;

    public EfRoomStore(TeamToolsDbContext db) => _db = db;

    public Task<Room?> FindByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default) =>
        WithPayload(_db.Rooms).FirstOrDefaultAsync(r => r.ShortCode == shortCode, cancellationToken);

    public Task<Room?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithPayload(_db.Rooms).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<bool> ShortCodeExistsAsync(string shortCode, CancellationToken cancellationToken = default) =>
        _db.Rooms.AnyAsync(r => r.ShortCode == shortCode, cancellationToken);

    public async Task AddAsync(Room room, CancellationToken cancellationToken = default)
    {
        _db.Rooms.Add(room);
        await SaveAsync(cancellationToken);
    }

    public Task UpdateAsync(Room room, CancellationToken cancellationToken = default) =>
        SaveAsync(cancellationToken);

    public async Task RemoveAsync(Room room, CancellationToken cancellationToken = default)
    {
        _db.Rooms.Remove(room);
        await SaveAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Room>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await WithPayload(_db.Rooms).ToListAsync(cancellationToken);

    // Narrow to rounds with a running timer in SQL, so the 1s expiry pass only materialises (and tracks)
    // that small subset rather than scanning every room every second. The DateTimeOffset deadline
    // comparison is applied in memory because SQLite's EF provider can't translate DateTimeOffset ordering;
    // at most a handful of running-timer rounds reach here. The returned entities are tracked so the
    // caller's reveal + UpdateAsync persists. See #14.
    public async Task<IReadOnlyList<Room>> GetRoomsWithExpiredTimerAsync(
        DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var running = await _db.Rooms
            .Include(r => r.Participants)
            .Include(r => r.PokerRound)
            // Voting (auto-reveal on expiry) and Discussion (auto re-vote on expiry) both run a countdown. #14/#9.
            .Where(r => r.PokerRound != null
                && (r.PokerRound.State == SessionState.Voting || r.PokerRound.State == SessionState.Discussion)
                && r.PokerRound.TimerDeadline != null)
            .ToListAsync(cancellationToken);

        return running.Where(r => r.PokerRound!.TimerDeadline <= asOf).ToList();
    }

    // Narrow to boards with a running phase countdown in SQL, so the 1s expiry pass loads those and
    // nothing else. This used to call GetAllAsync — every room in the database, with participants,
    // round history and the whole retro board graph, once a second, forever (#33). The DateTimeOffset
    // deadline comparison is applied in memory for the same reason as the poker sweep: SQLite's EF
    // provider can't translate DateTimeOffset ordering. Board only, no collections: the caller clears
    // the deadline and saves, then re-reads per recipient to broadcast.
    public async Task<IReadOnlyList<Room>> GetRoomsWithExpiredPhaseAsync(
        DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var running = await _db.Rooms
            .Include(r => r.RetroBoard)
            .Where(r => r.RetroBoard != null && r.RetroBoard.PhaseDeadline != null)
            .ToListAsync(cancellationToken);

        return running.Where(r => r.RetroBoard!.PhaseDeadline <= asOf).ToList();
    }


    // Narrowed in SQL from the start, applying #33's lesson rather than rediscovering it: this pass
    // runs once per second forever. Topics are included because expiry accumulates the time spent
    // on the current one before opening the extension vote.
    public async Task<IReadOnlyList<Room>> GetRoomsWithExpiredTimeboxAsync(
        DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var running = await _db.Rooms
            .Include(r => r.CoffeeBoard)
                .ThenInclude(b => b!.Topics)
            .Where(r => r.CoffeeBoard != null && r.CoffeeBoard.PhaseDeadline != null)
            .ToListAsync(cancellationToken);

        return running.Where(r => r.CoffeeBoard!.PhaseDeadline <= asOf).ToList();
    }
    // Projected existence check: no Include, no tracking — never materialises the aggregate. The global
    // query filter excludes soft-deleted rooms. See #17.
    public Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken cancellationToken = default) =>
        _db.Rooms
            .AsNoTracking()
            .AnyAsync(r => r.ShortCode == shortCode && r.ReactionsEnabled, cancellationToken);

    // Soft-deleted rooms sit behind the "not deleted" global query filter, so IgnoreQueryFilters() is
    // required to see them at all (#15). Narrows to DeletedAt != null in SQL (translatable everywhere);
    // the DeletedAt <= threshold comparison runs in memory, mirroring GetRoomsWithExpiredTimerAsync's
    // DateTimeOffset workaround above — at most a handful of soft-deleted rooms reach here per pass.
    public async Task<IReadOnlyList<Room>> GetSoftDeletedPastRetentionAsync(
        DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        var softDeleted = await WithPayload(_db.Rooms.IgnoreQueryFilters())
            .Where(r => r.DeletedAt != null)
            .ToListAsync(cancellationToken);

        return softDeleted.Where(r => r.DeletedAt <= threshold).ToList();
    }

    /// <summary>
    /// The standard aggregate load: participants plus the tool payload and its history. One place, so
    /// a new tool's payload is added to every read at once.
    /// </summary>
    private static IQueryable<Room> WithPayload(IQueryable<Room> rooms) =>
        rooms
            .Include(r => r.Participants)
            .Include(r => r.PokerRound)
                .ThenInclude(p => p!.RoundResults)
            .Include(r => r.RetroBoard)
                .ThenInclude(b => b!.Columns)
            .Include(r => r.RetroBoard)
                .ThenInclude(b => b!.Cards)
            .Include(r => r.RetroBoard)
                .ThenInclude(b => b!.Groups)
            .Include(r => r.RetroBoard)
                .ThenInclude(b => b!.Votes)
            .Include(r => r.RetroBoard)
                .ThenInclude(b => b!.Actions)
            .Include(r => r.CoffeeBoard)
                .ThenInclude(b => b!.Topics)
            .Include(r => r.CoffeeBoard)
                .ThenInclude(b => b!.Votes)
            .Include(r => r.CoffeeBoard)
                .ThenInclude(b => b!.ExtendVotes)
            .Include(r => r.CoffeeBoard)
                .ThenInclude(b => b!.Decisions)
            .Include(r => r.StandupBoard)
                .ThenInclude(b => b!.Questions)
            .Include(r => r.StandupBoard)
                .ThenInclude(b => b!.Entries)
            .Include(r => r.StandupBoard)
                .ThenInclude(b => b!.Blockers);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueNameViolation(ex))
        {
            throw new DuplicateNameException("A participant with that name already exists in this session.");
        }
    }

    private static bool IsUniqueNameViolation(DbUpdateException ex)
    {
        // SQLite surfaces: "UNIQUE constraint failed: Participants.RoomId, Participants.NormalizedName"
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            && message.Contains("NormalizedName", StringComparison.OrdinalIgnoreCase);
    }
}
