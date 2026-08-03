namespace PlanningPoker.Core;

/// <summary>
/// Tunable windows for the long-term session retention policy (#15), layered on the existing
/// <c>ClosedAt</c>/<c>DeletedAt</c> fields (#26). Bound in the composition root from configuration
/// ("Retention:*"); Core tests use the defaults. See <see cref="SessionMaintenanceService.PurgeAsync"/>.
/// </summary>
public sealed class RetentionOptions
{
    /// <summary>Months a closed (read-only) session is kept before it's auto soft-deleted.</summary>
    public int ClosedRetentionMonths { get; init; } = 12;

    /// <summary>Days a soft-deleted session is kept before it's permanently (hard) deleted.</summary>
    public int SoftDeleteRetentionDays { get; init; } = 30;

    /// <summary>
    /// Days a session may go without activity — and isn't closed or already soft-deleted — before
    /// it's auto soft-deleted.
    /// </summary>
    public int IdleRetentionDays { get; init; } = 30;
}
