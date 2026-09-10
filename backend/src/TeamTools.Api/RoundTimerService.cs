using Microsoft.AspNetCore.SignalR;
using TeamTools.Api.Hubs;
using TeamTools.Core.Poker;

namespace TeamTools.Api;

/// <summary>
/// Fires round-timer expiry: on a tight cadence it force-reveals sessions whose timer deadline has
/// passed and broadcasts the new snapshot, so the reveal lands promptly and every client agrees on
/// when time was up. The "is it due?" decision lives in <see cref="PokerTimerService"/>
/// (Core, unit-tested); the loop is <see cref="RoomSweepService"/>. See #14.
/// </summary>
public class RoundTimerService : RoomSweepService
{
    private readonly IHubContext<PokerHub> _hub;

    public RoundTimerService(
        IServiceScopeFactory scopeFactory,
        IHubContext<PokerHub> hub,
        ILogger<RoundTimerService> logger)
        : base(scopeFactory, logger)
    {
        _hub = hub;
    }

    // A second: a reveal that lands late is a reveal the room has already talked over.
    protected override TimeSpan Interval => TimeSpan.FromSeconds(1);

    protected override string SweepName => "Round-timer expiry";

    protected override async Task SweepAsync(IServiceProvider services, CancellationToken ct)
    {
        var timers = services.GetRequiredService<PokerTimerService>();

        foreach (var snapshot in await timers.ExpireDueRoundTimersAsync(ct))
        {
            await _hub.Clients.Group(PokerHub.GroupName(snapshot.ShortCode))
                .SendAsync("SessionUpdated", snapshot, ct);
        }
    }
}
