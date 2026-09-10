using Microsoft.AspNetCore.SignalR;
using TeamTools.Api.Hubs;
using TeamTools.Core.Retro;

namespace TeamTools.Api;

/// <summary>
/// Fires retro phase-countdown expiry (#23): on a tight cadence it clears elapsed countdowns and
/// re-broadcasts the board, so every client stops its ticker at the same moment. The "is it due?"
/// decision lives in <see cref="RetroPhaseTimerService"/> (Core, unit-tested); the loop is
/// <see cref="RoomSweepService"/>, shared with the poker round timer.
/// </summary>
public class RetroPhaseTimerBackgroundService : RoomSweepService
{
    private readonly IHubContext<RetroHub> _hub;
    private readonly ConnectionRegistry _connections;

    public RetroPhaseTimerBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHubContext<RetroHub> hub,
        ConnectionRegistry connections,
        ILogger<RetroPhaseTimerBackgroundService> logger)
        : base(scopeFactory, logger)
    {
        _hub = hub;
        _connections = connections;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(1);

    protected override string SweepName => "Retro phase-timer expiry";

    protected override async Task SweepAsync(IServiceProvider services, CancellationToken ct)
    {
        var timers = services.GetRequiredService<RetroPhaseTimerService>();
        var retro = services.GetRequiredService<RetroService>();

        foreach (var room in await timers.ExpireDuePhaseTimersAsync(ct))
        {
            // Per connection, not per group: a retro board is projected per viewer (#21).
            foreach (var (connectionId, userId) in _connections.InRoom(room.ShortCode))
            {
                var snapshot = await retro.GetByShortCodeAsync(room.ShortCode, userId, ct);
                if (snapshot is not null)
                {
                    await _hub.Clients.Client(connectionId).SendAsync("BoardUpdated", snapshot, ct);
                }
            }
        }
    }
}
