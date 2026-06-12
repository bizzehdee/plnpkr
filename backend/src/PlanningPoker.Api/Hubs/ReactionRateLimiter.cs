using System.Collections.Concurrent;
using PlanningPoker.Core;

namespace PlanningPoker.Api.Hubs;

/// <summary>
/// Per-connection sliding-window rate limit for emoji reactions (#17), so a client can't spam the
/// session group. In-memory singleton keyed by connection id; uses <see cref="IClock"/> so it's testable.
/// </summary>
public sealed class ReactionRateLimiter
{
    private const int MaxPerWindow = 5;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private readonly IClock _clock;
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new();

    public ReactionRateLimiter(IClock clock) => _clock = clock;

    /// <summary>Records a reaction for the connection and returns false if it's over the limit.</summary>
    public bool TryReact(string connectionId)
    {
        var now = _clock.UtcNow;
        var hits = _hits.GetOrAdd(connectionId, _ => new Queue<DateTimeOffset>());
        lock (hits)
        {
            while (hits.Count > 0 && now - hits.Peek() > Window)
            {
                hits.Dequeue();
            }

            if (hits.Count >= MaxPerWindow)
            {
                return false;
            }

            hits.Enqueue(now);
            return true;
        }
    }

    /// <summary>Drops a disconnected connection's window so the map doesn't grow unbounded.</summary>
    public void Forget(string connectionId) => _hits.TryRemove(connectionId, out _);
}
