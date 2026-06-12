using Microsoft.AspNetCore.SignalR;
using PlanningPoker.Api.Hubs;
using PlanningPoker.Core;

namespace PlanningPoker.Api;

/// <summary>
/// Fires round-timer expiry: on a tight cadence it force-reveals sessions whose timer deadline has
/// passed and broadcasts the new snapshot, so the reveal lands promptly and every client agrees on
/// when time was up. The "is it due?" decision lives in <see cref="SessionMaintenanceService"/>
/// (Core, unit-tested); this is just the scheduler. See #14.
/// </summary>
public class RoundTimerService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<PlanningPokerHub> _hub;
    private readonly ILogger<RoundTimerService> _logger;

    public RoundTimerService(
        IServiceScopeFactory scopeFactory,
        IHubContext<PlanningPokerHub> hub,
        ILogger<RoundTimerService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
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
                _logger.LogError(ex, "Round-timer expiry pass failed.");
            }
        }
    }

    private async Task ExpireOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<SessionMaintenanceService>();

        var revealed = await maintenance.ExpireDueRoundTimersAsync(ct);

        foreach (var snapshot in revealed)
        {
            await _hub.Clients.Group(PlanningPokerHub.GroupName(snapshot.ShortCode))
                .SendAsync("SessionUpdated", snapshot, ct);
        }
    }
}
