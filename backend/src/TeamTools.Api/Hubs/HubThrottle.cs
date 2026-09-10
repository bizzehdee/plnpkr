using System.Collections.Concurrent;
using TeamTools.Core;

namespace TeamTools.Api.Hubs;

/// <summary>
/// Per-connection sliding-window throttle for expensive/abusable hub actions — chiefly
/// session creation and joining over the anonymous, public hub (#3-abuse). Separate windows
/// per (connection, action) key. In-memory singleton; uses <see cref="IClock"/> so it's testable.
/// </summary>
public sealed class HubThrottle
{
    /// <summary>Default per-action limits: (max hits, window). Tuned for humans, hostile to scripts.</summary>
    public sealed class Options
    {
        public int CreatePerMinute { get; init; } = 10;
        public int JoinPerMinute { get; init; } = 30;
        /// <summary>Retro card adds (#21). Generous: a brainstorm is genuinely fast typing.</summary>
        public int CardsPerMinute { get; init; } = 60;
    }

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly IClock _clock;
    private readonly Options _options;
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new();

    public HubThrottle(IClock clock, Options? options = null)
    {
        _clock = clock;
        _options = options ?? new Options();
    }

    /// <summary>Records a create attempt for the connection; false when over the per-minute limit.</summary>
    public bool TryCreate(string connectionId) => Try($"{connectionId}:create", _options.CreatePerMinute);

    /// <summary>Records a join attempt for the connection; false when over the per-minute limit.</summary>
    public bool TryJoin(string connectionId) => Try($"{connectionId}:join", _options.JoinPerMinute);

    /// <summary>Records a retro card add; false when over the per-minute limit. See #21.</summary>
    public bool TryAddCard(string connectionId) => Try($"{connectionId}:card", _options.CardsPerMinute);

    private bool Try(string key, int max)
    {
        var now = _clock.UtcNow;
        var hits = _hits.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (hits)
        {
            while (hits.Count > 0 && now - hits.Peek() > Window)
            {
                hits.Dequeue();
            }

            if (hits.Count >= max)
            {
                return false;
            }

            hits.Enqueue(now);
            return true;
        }
    }

    /// <summary>Drops a disconnected connection's windows so the map doesn't grow unbounded.</summary>
    public void Forget(string connectionId)
    {
        _hits.TryRemove($"{connectionId}:create", out _);
        _hits.TryRemove($"{connectionId}:join", out _);
        _hits.TryRemove($"{connectionId}:card", out _);
    }
}
