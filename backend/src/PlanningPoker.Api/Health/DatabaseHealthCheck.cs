using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PlanningPoker.Data;

namespace PlanningPoker.Api.Health;

/// <summary>
/// Readiness check for the SQLite store: confirms the database is reachable AND that the schema is in
/// place by querying a real table — not merely that a file/connection exists. If migrations failed to
/// apply or the data volume isn't mounted/writable, this reports Unhealthy. See #3.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly PlanningPokerDbContext _db;

    public DatabaseHealthCheck(PlanningPokerDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _db.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("Cannot open the database.");
            }

            // Touch a migrated table so a missing/stale schema surfaces, not just connectivity.
            _ = await _db.Sessions.AnyAsync(cancellationToken);

            return HealthCheckResult.Healthy("Database reachable and queryable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database query failed.", ex);
        }
    }
}
