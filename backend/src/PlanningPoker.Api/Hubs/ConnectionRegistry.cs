using System.Collections.Concurrent;

namespace PlanningPoker.Api.Hubs;

/// <summary>
/// Tracks which session/user each live SignalR connection belongs to, so a dropped connection can
/// be cleaned up on disconnect. Singleton; keyed by connection id. See #34.
/// </summary>
public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();

    public void Track(string connectionId, string shortCode, string userId) =>
        _connections[connectionId] = new ConnectionInfo(shortCode, userId);

    /// <summary>Looks up the session/user a live connection belongs to, without removing it.</summary>
    public bool TryGet(string connectionId, out ConnectionInfo info) =>
        _connections.TryGetValue(connectionId, out info!);

    public bool TryRemove(string connectionId, out ConnectionInfo info) =>
        _connections.TryRemove(connectionId, out info!);
}

public readonly record struct ConnectionInfo(string ShortCode, string UserId);
