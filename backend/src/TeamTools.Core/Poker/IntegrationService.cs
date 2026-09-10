using TeamTools.Core.Contracts;
using TeamTools.Core.Integrations;
using TeamTools.Core.Models;

namespace TeamTools.Core.Poker;

/// <summary>
/// Per-provider feature toggles for the issue-tracker integration (#43). Each provider is
/// enabled independently; the whole feature is "on" when at least one provider is enabled.
/// </summary>
public sealed class IntegrationsOptions
{
    public ProviderIntegrationOptions Jira { get; set; } = new();
    public ProviderIntegrationOptions Ado { get; set; } = new();
    public ProviderIntegrationOptions GitHub { get; set; } = new();
    public ProviderIntegrationOptions GitLab { get; set; } = new();

    /// <summary>True when at least one provider is enabled (the feature shows up at all).</summary>
    public bool AnyEnabled => Jira.Enabled || Ado.Enabled || GitHub.Enabled || GitLab.Enabled;

    /// <summary>Whether the given provider is individually enabled.</summary>
    public bool IsEnabled(IntegrationProvider provider) => provider switch
    {
        IntegrationProvider.Jira => Jira.Enabled,
        IntegrationProvider.AzureDevOps => Ado.Enabled,
        IntegrationProvider.GitHub => GitHub.Enabled,
        IntegrationProvider.GitLab => GitLab.Enabled,
        _ => false,
    };
}

/// <summary>Settings for a single integration provider (#43).</summary>
public sealed class ProviderIntegrationOptions
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Orchestrates the optional issue-tracker integration: connect (validate + remember the token in
/// the in-memory store), disconnect, and link a ticket. Transport/HTTP stay in the tracker adapters;
/// this stays unit-testable against a fake <see cref="IIssueTracker"/>. See #4.
/// </summary>
public class IntegrationService
{
    private const int MaxQueueResults = 100;

    private readonly IRoomStore _store;
    private readonly IIssueTrackerFactory _trackers;
    private readonly IIntegrationConnectionStore _connections;
    private readonly IBoardUrlParser _urlParser;
    private readonly IClock _clock;
    private readonly IntegrationsOptions _options;

    public IntegrationService(
        IRoomStore store,
        IIssueTrackerFactory trackers,
        IIntegrationConnectionStore connections,
        IBoardUrlParser urlParser,
        IClock clock,
        IntegrationsOptions options)
    {
        _store = store;
        _trackers = trackers;
        _connections = connections;
        _urlParser = urlParser;
        _clock = clock;
        _options = options;
    }

    /// <summary>Validates the credentials, remembers the connection in memory, and links the provider.</summary>
    public async Task<IntegrationResult> ConnectAsync(
        string shortCode, string userId, IntegrationProvider provider, string baseUrl, string? email, string token,
        string? storyPointsField = null, CancellationToken ct = default)
    {
        if (!_options.IsEnabled(provider))
        {
            return IntegrationResult.Disabled();
        }

        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var fieldOverride = string.IsNullOrWhiteSpace(storyPointsField) ? null : storyPointsField.Trim();
        var connection = new TrackerConnection(provider, baseUrl.TrimEnd('/'), email, token,
            StoryPointsFieldOverride: fieldOverride);
        TrackerValidation validation;
        try
        {
            validation = await _trackers.For(provider).ValidateAsync(connection, ct);
        }
        catch (TrackerException ex)
        {
            return IntegrationResult.Fail(MapStatus(ex.Kind), ex.Message);
        }

        if (!validation.Ok)
        {
            return IntegrationResult.Fail(IntegrationStatus.AuthFailed, validation.Error ?? "Could not authenticate.");
        }

        _connections.Set(room!.Id, connection, validation.AccountName);
        room.PokerRound!.LinkedProvider = provider;
        room.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(room, ct);

        return IntegrationResult.Ok(PokerService.ToSnapshot(room), validation.AccountName);
    }

    public async Task<IntegrationResult> DisconnectAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        _connections.Remove(room!.Id);
        room.PokerRound!.LinkedProvider = null;
        room.PokerRound!.LinkedIssue = null;
        room.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(room, ct);

        return IntegrationResult.Ok(PokerService.ToSnapshot(room));
    }

    /// <summary>Looks up a ticket using the room's stored connection and links it (broadcast-safe).</summary>
    public async Task<IntegrationResult> LinkIssueAsync(string shortCode, string userId, string issueKey, CancellationToken ct = default)
    {
        if (!_options.AnyEnabled)
        {
            return IntegrationResult.Disabled();
        }

        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        if (!_connections.TryGet(room!.Id, out var connection))
        {
            return IntegrationResult.NotConnected();
        }

        if (!_options.IsEnabled(connection.Provider))
        {
            return IntegrationResult.Disabled();
        }

        try
        {
            await ApplyLinkedIssueAsync(room, connection, issueKey.Trim(), ct);
        }
        catch (TrackerException ex)
        {
            return IntegrationResult.Fail(MapStatus(ex.Kind), ex.Message);
        }

        room.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(room, ct);

        return IntegrationResult.Ok(PokerService.ToSnapshot(room));
    }

    /// <summary>Writes the agreed story points to the linked ticket, then refreshes it (#24).</summary>
    public async Task<IntegrationResult> SubmitStoryPointsAsync(string shortCode, string userId, double points, CancellationToken ct = default)
    {
        if (!_options.AnyEnabled)
        {
            return IntegrationResult.Disabled();
        }

        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        if (!_connections.TryGet(room!.Id, out var connection))
        {
            return IntegrationResult.NotConnected();
        }

        if (!_options.IsEnabled(connection.Provider))
        {
            return IntegrationResult.Disabled();
        }

        if (room.PokerRound!.LinkedIssue is not { } linked)
        {
            return IntegrationResult.Fail(IntegrationStatus.IssueNotFound, "Link a ticket before submitting story points.");
        }

        var tracker = _trackers.For(connection.Provider);
        try
        {
            await tracker.SetStoryPointsAsync(connection, linked.Key, points, ct);
            // Re-read so the displayed value reflects what the tracker actually stored.
            var refreshed = await tracker.GetIssueAsync(connection, linked.Key, ct);
            linked.StoryPoints = refreshed.CurrentStoryPoints ?? points;
            linked.StoryPointsFieldAvailable = refreshed.StoryPointsFieldAvailable;
        }
        catch (TrackerException ex)
        {
            return IntegrationResult.Fail(MapStatus(ex.Kind), ex.Message);
        }

        room.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(room, ct);
        return IntegrationResult.Ok(PokerService.ToSnapshot(room));
    }

    /// <summary>Loads a ticket queue from a board/query URL. See #38.</summary>
    public async Task<IntegrationResult> LoadQueueFromUrlAsync(string shortCode, string userId, string url, CancellationToken ct = default)
    {
        var query = _urlParser.Parse(url);
        if (query is null)
        {
            return IntegrationResult.Fail(IntegrationStatus.ProviderError,
                "That URL wasn't recognised — paste the ticket IDs instead.");
        }
        return await LoadQueueAsync(shortCode, userId, query, ct);
    }

    /// <summary>Loads a ticket queue from an explicit list of keys/ids. See #38.</summary>
    public async Task<IntegrationResult> LoadQueueFromKeysAsync(string shortCode, string userId, IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        var cleaned = keys.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (cleaned.Length == 0)
        {
            return IntegrationResult.Fail(IntegrationStatus.ProviderError, "No ticket IDs provided.");
        }
        return await LoadQueueAsync(shortCode, userId, new KeyListQuery(cleaned), ct);
    }

    private async Task<IntegrationResult> LoadQueueAsync(string shortCode, string userId, IssueQuery query, CancellationToken ct)
    {
        if (!_options.AnyEnabled)
        {
            return IntegrationResult.Disabled();
        }

        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        if (!_connections.TryGet(room!.Id, out var connection))
        {
            return IntegrationResult.NotConnected();
        }

        if (!_options.IsEnabled(connection.Provider))
        {
            return IntegrationResult.Disabled();
        }

        IReadOnlyList<IssueSummary> summaries;
        try
        {
            summaries = await _trackers.For(connection.Provider).SearchAsync(connection, query, MaxQueueResults, ct);
        }
        catch (TrackerException ex)
        {
            return IntegrationResult.Fail(MapStatus(ex.Kind), ex.Message);
        }

        room.PokerRound!.TicketQueue = summaries
            .Select(s => new QueuedTicket { Key = s.Key, Title = s.Title, Status = s.Status, StoryPoints = s.StoryPoints, Url = s.Url })
            .ToList();

        // A single id behaves like the old "link ticket"; for a list we open the first so there's
        // always a ticket on the table. Best-effort — a fetch failure still leaves the queue loaded.
        var selected = room.PokerRound!.LinkedIssue?.Key;
        var stillInQueue = selected is not null && room.PokerRound!.TicketQueue.Any(t => string.Equals(t.Key, selected, StringComparison.OrdinalIgnoreCase));
        if (room.PokerRound!.TicketQueue.Count > 0 && !stillInQueue)
        {
            try
            {
                await ApplyLinkedIssueAsync(room, connection, room.PokerRound!.TicketQueue[0].Key, ct);
            }
            catch (TrackerException)
            {
                // Keep the queue even if auto-linking the first ticket fails.
            }
        }

        room.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(room, ct);
        return IntegrationResult.Ok(PokerService.ToSnapshot(room));
    }

    /// <summary>Fetches a ticket and sets it as the room's linked issue + current story (no persist).</summary>
    private async Task ApplyLinkedIssueAsync(Room room, TrackerConnection connection, string issueKey, CancellationToken ct)
    {
        var issue = await _trackers.For(connection.Provider).GetIssueAsync(connection, issueKey, ct);
        room.PokerRound!.LinkedIssue = new LinkedIssue
        {
            Key = issue.Key,
            Title = issue.Title,
            Description = issue.Description,
            Url = issue.Url,
            StoryPoints = issue.CurrentStoryPoints,
            StoryPointsFieldAvailable = issue.StoryPointsFieldAvailable,
        };
        // Surface the ticket title as the current story too, so it shows everywhere the story does.
        room.PokerRound!.CurrentStory = issue.Title;
    }

    /// <summary>Clears the ticket queue (organiser-only).</summary>
    public async Task<IntegrationResult> ClearQueueAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        room!.PokerRound!.TicketQueue.Clear();
        room.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(room, ct);
        return IntegrationResult.Ok(PokerService.ToSnapshot(room));
    }

    /// <summary>True if this room currently has a live (in-memory) connection.</summary>
    public bool IsConnected(Guid roomId) => _connections.IsConnected(roomId);

    public string? ConnectedAccount(Guid roomId) => _connections.GetAccountName(roomId);

    private async Task<(Room? Room, IntegrationResult? Error)> LoadForControlAsync(
        string shortCode, string userId, CancellationToken ct)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        if (room is null)
        {
            return (null, IntegrationResult.NotFound());
        }

        if (room.Participants.All(p => p.UserId != userId))
        {
            return (null, IntegrationResult.NotParticipant());
        }

        // Organiser, or anyone when the room has no organiser (same rule as reveal/reset, #10).
        if (room.OrganiserUserId is not null && room.OrganiserUserId != userId)
        {
            return (null, IntegrationResult.NotOrganiser());
        }

        // A closed room is read-only (#26) — no integration changes.
        if (room.ClosedAt is not null)
        {
            return (null, IntegrationResult.SessionClosed());
        }

        return (room, null);
    }

    private static IntegrationStatus MapStatus(TrackerErrorKind kind) => kind switch
    {
        TrackerErrorKind.Unauthorized => IntegrationStatus.AuthFailed,
        TrackerErrorKind.Forbidden => IntegrationStatus.AuthFailed,
        TrackerErrorKind.NotFound => IntegrationStatus.IssueNotFound,
        _ => IntegrationStatus.ProviderError,
    };
}
