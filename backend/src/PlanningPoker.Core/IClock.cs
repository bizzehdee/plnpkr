namespace PlanningPoker.Core;

/// <summary>
/// Abstraction over the current time so time-dependent behaviour (idle eviction, timestamps)
/// is deterministically testable without touching the wall clock.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default <see cref="IClock"/> backed by the system clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
