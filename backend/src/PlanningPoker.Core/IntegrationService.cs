using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Integrations;
using PlanningPoker.Core.Models;

namespace PlanningPoker.Core;

/// <summary>
/// Per-provider feature toggles for the issue-tracker integration (#43). Each provider is
/// enabled independently; the whole feature is "on" when at least one provider is enabled.
/// </summary>
public sealed class IntegrationsOptions
{
    public ProviderIntegrationOptions Jira { get; set; } = new();
    public ProviderIntegrationOptions Ado { get; set; } = new();

    /// <summary>True when at least one provider is enabled (the feature shows up at all).</summary>
    public bool AnyEnabled => Jira.Enabled || Ado.Enabled;

    /// <summary>Whether the given provider is individually enabled.</summary>
    public bool IsEnabled(IntegrationProvider provider) => provider switch
    {
        IntegrationProvider.Jira => Jira.Enabled,
        IntegrationProvider.AzureDevOps => Ado.Enabled,
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

    private readonly ISessionStore _store;
    private readonly IIssueTrackerFactory _trackers;
    private readonly IIntegrationConnectionStore _connections;
    private readonly IBoardUrlParser _urlParser;
    private readonly IClock _clock;
    private readonly IntegrationsOptions _options;

    public IntegrationService(
        ISessionStore store,
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

        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
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

        _connections.Set(session!.Id, connection, validation.AccountName);
        session.LinkedProvider = provider;
        session.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);

        return IntegrationResult.Ok(SessionService.ToSnapshot(session), validation.AccountName);
    }

    public async Task<IntegrationResult> DisconnectAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        _connections.Remove(session!.Id);
        session.LinkedProvider = null;
        session.LinkedIssue = null;
        session.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);

        return IntegrationResult.Ok(SessionService.ToSnapshot(session));
    }

    /// <summary>Looks up a ticket using the session's stored connection and links it (broadcast-safe).</summary>
    public async Task<IntegrationResult> LinkIssueAsync(string shortCode, string userId, string issueKey, CancellationToken ct = default)
    {
        if (!_options.AnyEnabled)
        {
            return IntegrationResult.Disabled();
        }

        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        if (!_connections.TryGet(session!.Id, out var connection))
        {
            return IntegrationResult.NotConnected();
        }

        if (!_options.IsEnabled(connection.Provider))
        {
            return IntegrationResult.Disabled();
        }

        try
        {
            await ApplyLinkedIssueAsync(session, connection, issueKey.Trim(), ct);
        }
        catch (TrackerException ex)
        {
            return IntegrationResult.Fail(MapStatus(ex.Kind), ex.Message);
        }

        session.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);

        return IntegrationResult.Ok(SessionService.ToSnapshot(session));
    }

    /// <summary>Writes the agreed story points to the linked ticket, then refreshes it (#24).</summary>
    public async Task<IntegrationResult> SubmitStoryPointsAsync(string shortCode, string userId, double points, CancellationToken ct = default)
    {
        if (!_options.AnyEnabled)
        {
            return IntegrationResult.Disabled();
        }

        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        if (!_connections.TryGet(session!.Id, out var connection))
        {
            return IntegrationResult.NotConnected();
        }

        if (!_options.IsEnabled(connection.Provider))
        {
            return IntegrationResult.Disabled();
        }

        if (session.LinkedIssue is not { } linked)
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

        session.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);
        return IntegrationResult.Ok(SessionService.ToSnapshot(session));
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

        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        if (!_connections.TryGet(session!.Id, out var connection))
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

        session.TicketQueue = summaries
            .Select(s => new QueuedTicket { Key = s.Key, Title = s.Title, Status = s.Status, StoryPoints = s.StoryPoints, Url = s.Url })
            .ToList();

        // A single id behaves like the old "link ticket"; for a list we open the first so there's
        // always a ticket on the table. Best-effort — a fetch failure still leaves the queue loaded.
        var selected = session.LinkedIssue?.Key;
        var stillInQueue = selected is not null && session.TicketQueue.Any(t => string.Equals(t.Key, selected, StringComparison.OrdinalIgnoreCase));
        if (session.TicketQueue.Count > 0 && !stillInQueue)
        {
            try
            {
                await ApplyLinkedIssueAsync(session, connection, session.TicketQueue[0].Key, ct);
            }
            catch (TrackerException)
            {
                // Keep the queue even if auto-linking the first ticket fails.
            }
        }

        session.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);
        return IntegrationResult.Ok(SessionService.ToSnapshot(session));
    }

    /// <summary>Fetches a ticket and sets it as the session's linked issue + current story (no persist).</summary>
    private async Task ApplyLinkedIssueAsync(Models.Session session, TrackerConnection connection, string issueKey, CancellationToken ct)
    {
        var issue = await _trackers.For(connection.Provider).GetIssueAsync(connection, issueKey, ct);
        session.LinkedIssue = new LinkedIssue
        {
            Key = issue.Key,
            Title = issue.Title,
            Description = issue.Description,
            Url = issue.Url,
            StoryPoints = issue.CurrentStoryPoints,
            StoryPointsFieldAvailable = issue.StoryPointsFieldAvailable,
        };
        // Surface the ticket title as the current story too, so it shows everywhere the story does.
        session.CurrentStory = issue.Title;
    }

    /// <summary>Clears the ticket queue (organiser-only).</summary>
    public async Task<IntegrationResult> ClearQueueAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        session!.TicketQueue.Clear();
        session.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);
        return IntegrationResult.Ok(SessionService.ToSnapshot(session));
    }

    /// <summary>True if this session currently has a live (in-memory) connection.</summary>
    public bool IsConnected(Guid sessionId) => _connections.IsConnected(sessionId);

    public string? ConnectedAccount(Guid sessionId) => _connections.GetAccountName(sessionId);

    private async Task<(Session? Session, IntegrationResult? Error)> LoadForControlAsync(
        string shortCode, string userId, CancellationToken ct)
    {
        var session = await _store.FindByShortCodeAsync(shortCode, ct);
        if (session is null)
        {
            return (null, IntegrationResult.NotFound());
        }

        if (session.Participants.All(p => p.UserId != userId))
        {
            return (null, IntegrationResult.NotParticipant());
        }

        // Organiser, or anyone when the session has no organiser (same rule as reveal/reset, #10).
        if (session.OrganiserUserId is not null && session.OrganiserUserId != userId)
        {
            return (null, IntegrationResult.NotOrganiser());
        }

        // A closed session is read-only (#26) — no integration changes.
        if (session.ClosedAt is not null)
        {
            return (null, IntegrationResult.SessionClosed());
        }

        return (session, null);
    }

    private static IntegrationStatus MapStatus(TrackerErrorKind kind) => kind switch
    {
        TrackerErrorKind.Unauthorized => IntegrationStatus.AuthFailed,
        TrackerErrorKind.Forbidden => IntegrationStatus.AuthFailed,
        TrackerErrorKind.NotFound => IntegrationStatus.IssueNotFound,
        _ => IntegrationStatus.ProviderError,
    };
}
