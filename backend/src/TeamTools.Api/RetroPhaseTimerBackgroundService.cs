using Microsoft.AspNetCore.SignalR;
using TeamTools.Api.Hubs;
using TeamTools.Core.Retro;

namespace TeamTools.Api;

/// <summary>
/// Fires retro phase-countdown expiry (#23): on a tight cadence it clears elapsed countdowns and
/// re-broadcasts the board, so every client stops its ticker at the same moment. The "is it due?"
/// decision lives in <see cref="RetroPhaseTimerService"/> (Core, unit-tested); this is just the
/// scheduler — the retro sibling of <see cref="RoundTimerService"/>.
/// </summary>
public class RetroPhaseTimerBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<RetroHub> _hub;
    private readonly ConnectionRegistry _connections;
    private readonly ILogger<RetroPhaseTimerBackgroundService> _logger;

    public RetroPhaseTimerBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHubContext<RetroHub> hub,
        ConnectionRegistry connections,
        ILogger<RetroPhaseTimerBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _connections = connections;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ExpireOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retro phase-timer expiry pass failed.");
            }
        }
    }

    private async Task ExpireOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var timers = scope.ServiceProvider.GetRequiredService<RetroPhaseTimerService>();
        var retro = scope.ServiceProvider.GetRequiredService<RetroService>();

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
