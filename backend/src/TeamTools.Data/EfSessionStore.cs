using Microsoft.EntityFrameworkCore;
using TeamTools.Core;
using TeamTools.Core.Models;

namespace TeamTools.Data;

/// <summary>
/// EF Core / SQLite implementation of <see cref="ISessionStore"/>. Loads the session aggregate with
/// its participants (tracked, so collection edits persist on save) and translates the unique-name
/// constraint violation into <see cref="DuplicateNameException"/>.
/// </summary>
public class EfSessionStore : ISessionStore
{
    private readonly TeamToolsDbContext _db;

    public EfSessionStore(TeamToolsDbContext db) => _db = db;

    public Task<Session?> FindByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default) =>
        _db.Sessions
            .Include(s => s.Participants)
            .Include(s => s.RoundResults)
            .FirstOrDefaultAsync(s => s.ShortCode == shortCode, cancellationToken);

    public Task<Session?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Sessions
            .Include(s => s.Participants)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<bool> ShortCodeExistsAsync(string shortCode, CancellationToken cancellationToken = default) =>
        _db.Sessions.AnyAsync(s => s.ShortCode == shortCode, cancellationToken);

    public async Task AddAsync(Session session, CancellationToken cancellationToken = default)
    {
        _db.Sessions.Add(session);
        await SaveAsync(cancellationToken);
    }

    public Task UpdateAsync(Session session, CancellationToken cancellationToken = default) =>
        SaveAsync(cancellationToken);

    public async Task RemoveAsync(Session session, CancellationToken cancellationToken = default)
    {
        _db.Sessions.Remove(session);
        await SaveAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Session>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.Sessions.Include(s => s.Participants).ToListAsync(cancellationToken);

    // Narrow to sessions with a running timer in SQL, so the 1s expiry pass only materialises (and tracks)
    // that small subset rather than scanning every session every second. The DateTimeOffset deadline
    // comparison is applied in memory because SQLite's EF provider can't translate DateTimeOffset ordering;
    // at most a handful of running-timer sessions reach here. The returned entities are tracked so the
    // caller's reveal + UpdateAsync persists. See #14.
    public async Task<IReadOnlyList<Session>> GetSessionsWithExpiredTimerAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var running = await _db.Sessions
            .Include(s => s.Participants)
            // Voting (auto-reveal on expiry) and Discussion (auto re-vote on expiry) both run a countdown. #14/#9.
            .Where(s => (s.State == SessionState.Voting || s.State == SessionState.Discussion) && s.TimerDeadline != null)
            .ToListAsync(cancellationToken);

        return running.Where(s => s.TimerDeadline <= asOf).ToList();
    }

    // Projected existence check: no Include, no tracking — never materialises the aggregate. The global
    // query filter excludes soft-deleted sessions. See #17.
    public Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken cancellationToken = default) =>
        _db.Sessions
            .AsNoTracking()
            .AnyAsync(s => s.ShortCode == shortCode && s.ReactionsEnabled, cancellationToken);

    // Soft-deleted sessions sit behind the "not deleted" global query filter, so IgnoreQueryFilters() is
    // required to see them at all (#15). Narrows to DeletedAt != null in SQL (translatable everywhere);
    // the DeletedAt <= threshold comparison runs in memory, mirroring GetSessionsWithExpiredTimerAsync's
    // DateTimeOffset workaround above — at most a handful of soft-deleted sessions reach here per pass.
    public async Task<IReadOnlyList<Session>> GetSoftDeletedPastRetentionAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        var softDeleted = await _db.Sessions
            .IgnoreQueryFilters()
            .Include(s => s.Participants)
            .Include(s => s.RoundResults)
            .Where(s => s.DeletedAt != null)
            .ToListAsync(cancellationToken);

        return softDeleted.Where(s => s.DeletedAt <= threshold).ToList();
    }

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
        // SQLite surfaces: "UNIQUE constraint failed: Participants.SessionId, Participants.NormalizedName"
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            && message.Contains("NormalizedName", StringComparison.OrdinalIgnoreCase);
    }
}
