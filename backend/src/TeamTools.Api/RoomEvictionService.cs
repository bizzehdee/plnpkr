using Microsoft.AspNetCore.SignalR;
using TeamTools.Api.Hubs;
using TeamTools.Core.Poker;
using TeamTools.Core;
using TeamTools.Core.Models;

namespace TeamTools.Api;

/// <summary>
/// Periodically purges away disconnected-past-grace participants and applies the retention policy
/// (#15), broadcasting the result so live clients update (or are told the session closed). The
/// "is this stale?" decision lives in <see cref="RoomMaintenanceService"/> (Core, unit-tested); this
/// is just the retention config and the broadcast. The loop is <see cref="RoomSweepService"/>. #37.
/// </summary>
public class RoomEvictionService : RoomSweepService
{
    private static readonly TimeSpan DisconnectGrace = TimeSpan.FromMinutes(2);

    private readonly IHubContext<PokerHub> _hub;
    private readonly RetentionOptions _retention;

    public RoomEvictionService(
        IServiceScopeFactory scopeFactory,
        IHubContext<PokerHub> hub,
        RetentionOptions retention,
        ILogger<RoomEvictionService> logger)
        : base(scopeFactory, logger)
    {
        _hub = hub;
        _retention = retention;
    }

    // A minute, not a second: nothing here is time-critical, and unlike the countdown sweeps this
    // one genuinely does load every room with its whole graph in order to delete it (#33).
    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override string SweepName => "Room eviction";

    protected override async Task SweepAsync(IServiceProvider services, CancellationToken ct)
    {
        var maintenance = services.GetRequiredService<RoomMaintenanceService>();

        var report = await maintenance.PurgeAsync(DisconnectGrace, _retention, ct);

        // The purge is tool-agnostic and hands back rooms; each is projected through its own tool
        // snapshot and broadcast on that tool's event (#19).
        foreach (var room in report.UpdatedRooms)
        {
            if (room.Tool == RoomTool.Poker)
            {
                await _hub.Clients.Group(PokerHub.GroupName(room.ShortCode))
                    .SendAsync("SessionUpdated", PokerService.ToSnapshot(room), ct);
            }
        }

        foreach (var shortCode in report.RemovedShortCodes)
        {
            await _hub.Clients.Group(PokerHub.GroupName(shortCode))
                .SendAsync("SessionClosed", ct);
        }
    }
}
