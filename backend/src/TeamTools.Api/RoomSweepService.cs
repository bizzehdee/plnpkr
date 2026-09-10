namespace TeamTools.Api;

/// <summary>
/// The scheduling half of a periodic room sweep (#34): a fixed-cadence loop that resolves a scoped
/// service, does one pass, and never dies of a single failed pass.
/// <para>
/// Extracted because all three sweeps — the poker round timer (#14), the retro phase countdown (#23)
/// and idle eviction (#37) — had their own copy of this loop, differing only in the interval and the
/// body. A fourth tool would have written it a fourth time, and the failure mode of getting it wrong
/// is silent: a pass that throws without the catch takes the whole background service down for the
/// life of the process, and nothing else notices.
/// </para>
/// <para>
/// What each sweep <b>keeps</b> is everything that matters: which rooms are due (a narrowed store
/// query per tool, #33), what expiry does, and who the result is broadcast to — poker to the room's
/// group, a retro per connection because its board is projected per viewer (#21).
/// </para>
/// </summary>
public abstract class RoomSweepService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    protected RoomSweepService(IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>How often to sweep.</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>What this sweep is, for the log line when a pass fails.</summary>
    protected abstract string SweepName { get; }

    /// <summary>
    /// One pass. <paramref name="services"/> is a fresh scope, so a sweep gets its own
    /// <c>DbContext</c> and cannot leak tracked entities from the previous tick into this one.
    /// </summary>
    protected abstract Task SweepAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await SweepAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad pass must not end the loop: the next tick tries again.
                _logger.LogError(ex, "{Sweep} pass failed.", SweepName);
            }
        }
    }
}
