using Microsoft.AspNetCore.SignalR;
using TeamTools.Api.Hubs;
using TeamTools.Core;

namespace TeamTools.Api;

/// <summary>
/// Periodically purges away disconnected-past-grace participants and applies the retention policy
/// (#15), broadcasting the result so live clients update (or are told the session closed). The
/// "is this stale?" decision lives in <see cref="SessionMaintenanceService"/> (Core, unit-tested);
/// this is just the scheduler + retention config. See #37.
/// </summary>
public class SessionEvictionService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DisconnectGrace = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<PokerHub> _hub;
    private readonly RetentionOptions _retention;
    private readonly ILogger<SessionEvictionService> _logger;

    public SessionEvictionService(
        IServiceScopeFactory scopeFactory,
        IHubContext<PokerHub> hub,
        RetentionOptions retention,
        ILogger<SessionEvictionService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _retention = retention;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PurgeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Session eviction pass failed.");
            }
        }
    }

    private async Task PurgeOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<SessionMaintenanceService>();

        var report = await maintenance.PurgeAsync(DisconnectGrace, _retention, ct);

        foreach (var snapshot in report.UpdatedSessions)
        {
            await _hub.Clients.Group(PokerHub.GroupName(snapshot.ShortCode))
                .SendAsync("SessionUpdated", snapshot, ct);
        }

        foreach (var shortCode in report.RemovedShortCodes)
        {
            await _hub.Clients.Group(PokerHub.GroupName(shortCode))
                .SendAsync("SessionClosed", ct);
        }
    }
}
