using TeamTools.Core.Models;

namespace TeamTools.Core;

/// <summary>
/// Idle eviction + long-term retention (#15): removes participants who have been disconnected past a
/// grace period, and applies the retention policy layered on <see cref="Room.ClosedAt"/>/
/// <see cref="Room.DeletedAt"/> (#26) — a closed room is soft-deleted <see
/// cref="RetentionOptions.ClosedRetentionMonths"/> months after closing; a room that's neither
/// closed nor already soft-deleted is soft-deleted after <see
/// cref="RetentionOptions.IdleRetentionDays"/> days of no activity; a soft-deleted room is
/// permanently (hard) deleted <see cref="RetentionOptions.SoftDeleteRetentionDays"/> days later.
/// Clock-driven so it is fully unit-testable. See #37.
/// <para>
/// Room-level by design (#19): it treats every room the same regardless of tool, which is why the
/// retention policy covers retro boards without a second implementation. It returns the affected
/// <see cref="Room"/> objects rather than snapshots, leaving projection to the tool.
/// </para>
/// </summary>
public class RoomMaintenanceService
{
    private readonly IRoomStore _store;
    private readonly IClock _clock;

    public RoomMaintenanceService(IRoomStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    /// <param name="disconnectGrace">How long a disconnected participant is kept for reconnect.</param>
    /// <param name="retention">The soft/hard-delete windows for the retention policy (#15).</param>
    public async Task<PurgeReport> PurgeAsync(
        TimeSpan disconnectGrace, RetentionOptions retention, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var removedShortCodes = new List<string>();
        var updatedRooms = new List<Room>();

        foreach (var room in await _store.GetAllAsync(ct))
        {
            var stale = room.Participants
                .Where(p => !p.IsConnected && p.LastSeenAt + disconnectGrace <= now)
                .ToList();

            foreach (var p in stale)
            {
                room.Participants.Remove(p);
                if (room.OrganiserUserId == p.UserId)
                {
                    // The organiser was evicted while away → fall back to no-organiser. See #39.
                    room.OrganiserUserId = null;
                }
            }

            // Retention (#15): a closed room auto soft-deletes after ClosedRetentionMonths; a room
            // that's neither closed nor already soft-deleted auto soft-deletes after IdleRetentionDays
            // of no activity. `AddMonths` (not a fixed day count) so "12 months" tracks calendar months.
            var shouldSoftDelete =
                (room.ClosedAt is { } closedAt && closedAt.AddMonths(retention.ClosedRetentionMonths) <= now)
                || (room.ClosedAt is null && room.LastActivityAt.AddDays(retention.IdleRetentionDays) <= now);

            if (shouldSoftDelete)
            {
                // Same effect as an organiser-triggered delete: gone from every read behind the global
                // query filter, so connected clients are told it's closed, not sent a snapshot.
                room.DeletedAt = now;
                await _store.UpdateAsync(room, ct);
                removedShortCodes.Add(room.ShortCode);
            }
            else if (stale.Count > 0)
            {
                await _store.UpdateAsync(room, ct);
                updatedRooms.Add(room);
            }
        }

        // Soft-deleted rooms past SoftDeleteRetentionDays are hard-deleted (row + payload gone).
        var hardDeleteThreshold = now.AddDays(-retention.SoftDeleteRetentionDays);
        foreach (var room in await _store.GetSoftDeletedPastRetentionAsync(hardDeleteThreshold, ct))
        {
            await _store.RemoveAsync(room, ct);
            removedShortCodes.Add(room.ShortCode);
        }

        return new PurgeReport(removedShortCodes, updatedRooms);
    }
}

/// <summary>
/// Outcome of a purge. <see cref="RemovedShortCodes"/> covers both hard-deleted rooms and rooms
/// newly soft-deleted this pass — to a connected client both mean "gone" (broadcast SessionClosed for
/// each). <see cref="UpdatedRooms"/> is still-alive rooms whose participant list changed only; the
/// caller projects each through its tool's snapshot and broadcasts that.
/// </summary>
public record PurgeReport(
    IReadOnlyList<string> RemovedShortCodes,
    IReadOnlyList<Room> UpdatedRooms);
