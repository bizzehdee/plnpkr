using Microsoft.AspNetCore.SignalR;
using TeamTools.Api.Hubs;
using TeamTools.Core.Coffee;

namespace TeamTools.Api;

/// <summary>
/// Fires Lean Coffee timebox expiry (#35): when a topic's time runs out it opens the extension vote
/// and re-broadcasts, so every client stops its ticker and sees the same question at the same
/// moment. The decision lives in <see cref="CoffeeTimerService"/>; the loop is the shared
/// <see cref="RoomSweepService"/> (#34) — the third tool onto it, and the first that did not have to
/// write it.
/// </summary>
public class CoffeeTimeboxBackgroundService : RoomSweepService
{
    private readonly IHubContext<CoffeeHub> _hub;
    private readonly ConnectionRegistry _connections;

    public CoffeeTimeboxBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHubContext<CoffeeHub> hub,
        ConnectionRegistry connections,
        ILogger<CoffeeTimeboxBackgroundService> logger)
        : base(scopeFactory, logger)
    {
        _hub = hub;
        _connections = connections;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(1);

    protected override string SweepName => "Coffee timebox expiry";

    protected override async Task SweepAsync(IServiceProvider services, CancellationToken ct)
    {
        var timers = services.GetRequiredService<CoffeeTimerService>();
        var coffee = services.GetRequiredService<CoffeeService>();

        foreach (var room in await timers.ExpireDueTimeboxesAsync(ct))
        {
            // Per connection, not per group: a coffee board is projected per viewer (#35).
            foreach (var (connectionId, userId) in _connections.InRoom(room.ShortCode))
            {
                var snapshot = await coffee.GetByShortCodeAsync(room.ShortCode, userId, ct);
                if (snapshot is not null)
                {
                    await _hub.Clients.Client(connectionId).SendAsync("BoardUpdated", snapshot, ct);
                }
            }
        }
    }
}
