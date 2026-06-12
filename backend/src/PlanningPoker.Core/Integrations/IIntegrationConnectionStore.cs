using System.Collections.Concurrent;

namespace PlanningPoker.Core.Integrations;

/// <summary>
/// Holds the per-session tracker connection (incl. the secret token) in server memory only — never
/// persisted server-side, never broadcast. Evicted with the session / on app restart. See #4.
/// </summary>
public interface IIntegrationConnectionStore
{
    void Set(Guid sessionId, TrackerConnection connection, string? accountName);
    bool TryGet(Guid sessionId, out TrackerConnection connection);
    string? GetAccountName(Guid sessionId);
    bool IsConnected(Guid sessionId);
    void Remove(Guid sessionId);
}

/// <summary>Default in-memory implementation (singleton). No framework dependencies.</summary>
public sealed class InMemoryIntegrationConnectionStore : IIntegrationConnectionStore
{
    private sealed record Entry(TrackerConnection Connection, string? AccountName);

    private readonly ConcurrentDictionary<Guid, Entry> _bySession = new();

    public void Set(Guid sessionId, TrackerConnection connection, string? accountName) =>
        _bySession[sessionId] = new Entry(connection, accountName);

    public bool TryGet(Guid sessionId, out TrackerConnection connection)
    {
        if (_bySession.TryGetValue(sessionId, out var entry))
        {
            connection = entry.Connection;
            return true;
        }

        connection = null!;
        return false;
    }

    public string? GetAccountName(Guid sessionId) =>
        _bySession.TryGetValue(sessionId, out var entry) ? entry.AccountName : null;

    public bool IsConnected(Guid sessionId) => _bySession.ContainsKey(sessionId);

    public void Remove(Guid sessionId) => _bySession.TryRemove(sessionId, out _);
}
