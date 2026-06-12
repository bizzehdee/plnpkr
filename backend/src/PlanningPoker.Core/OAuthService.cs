using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Core;

public enum OAuthStatus
{
    Ok,
    Disabled,
    NotConfigured,
    SessionNotFound,
    NotParticipant,
    NotOrganiser,
    InvalidState,
    Failed,
}

public record OAuthAuthorizeResult(OAuthStatus Status, string? AuthorizationUrl, string? Error)
{
    public static OAuthAuthorizeResult Ok(string url) => new(OAuthStatus.Ok, url, null);
    public static OAuthAuthorizeResult Fail(OAuthStatus status, string error) => new(status, null, error);
}

public record OAuthCompleteResult(
    OAuthStatus Status, string? ShortCode, string? UserId, string? AccountName, SessionSnapshot? Session, string? Error)
{
    public static OAuthCompleteResult Ok(string shortCode, string userId, string? account, SessionSnapshot session) =>
        new(OAuthStatus.Ok, shortCode, userId, account, session, null);
    public static OAuthCompleteResult Fail(OAuthStatus status, string error) =>
        new(status, null, null, null, null, error);
}

/// <summary>
/// Orchestrates the per-session OAuth "log in to the provider" flow (#4): start (validate +
/// PKCE + build authorize URL) and complete (exchange code + store the connection in memory). The
/// HTTP/token specifics live in the provider's <see cref="IOAuthFlow"/>; this stays unit-testable.
/// </summary>
public class OAuthService
{
    private readonly ISessionStore _store;
    private readonly IOAuthFlowProvider _flows;
    private readonly IOAuthFlowStore _pending;
    private readonly IIntegrationConnectionStore _connections;
    private readonly IClock _clock;
    private readonly IntegrationsOptions _options;

    public OAuthService(
        ISessionStore store,
        IOAuthFlowProvider flows,
        IOAuthFlowStore pending,
        IIntegrationConnectionStore connections,
        IClock clock,
        IntegrationsOptions options)
    {
        _store = store;
        _flows = flows;
        _pending = pending;
        _connections = connections;
        _clock = clock;
        _options = options;
    }

    /// <summary>True if the provider has an OAuth flow that is configured (client id/secret present).</summary>
    public bool IsConfigured(IntegrationProvider provider) => _flows.For(provider)?.IsConfigured == true;

    public async Task<OAuthAuthorizeResult> AuthorizeAsync(
        string shortCode, string userId, IntegrationProvider provider, string redirectUri,
        string? storyPointsField = null, CancellationToken ct = default)
    {
        if (!_options.IsEnabled(provider))
        {
            return OAuthAuthorizeResult.Fail(OAuthStatus.Disabled, "Integration is disabled.");
        }

        var flow = _flows.For(provider);
        if (flow is null || !flow.IsConfigured)
        {
            return OAuthAuthorizeResult.Fail(OAuthStatus.NotConfigured, "OAuth is not configured for this provider.");
        }

        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is { } status)
        {
            return OAuthAuthorizeResult.Fail(status, "You cannot connect this session.");
        }

        var fieldOverride = string.IsNullOrWhiteSpace(storyPointsField) ? null : storyPointsField.Trim();
        var (verifier, challenge) = Pkce.Create();
        var state = _pending.Create(provider, session!.ShortCode, userId, verifier, _clock.UtcNow, fieldOverride);
        return OAuthAuthorizeResult.Ok(flow.BuildAuthorizationUrl(redirectUri, state, challenge));
    }

    public async Task<OAuthCompleteResult> CompleteAsync(string state, string code, string redirectUri, CancellationToken ct = default)
    {
        if (!_pending.TryConsume(state, _clock.UtcNow, out var pending))
        {
            return OAuthCompleteResult.Fail(OAuthStatus.InvalidState, "The login session expired or is invalid — please try again.");
        }

        if (!_options.IsEnabled(pending.Provider))
        {
            return OAuthCompleteResult.Fail(OAuthStatus.Disabled, "Integration is disabled.");
        }

        var flow = _flows.For(pending.Provider);
        if (flow is null)
        {
            return OAuthCompleteResult.Fail(OAuthStatus.NotConfigured, "OAuth is not configured for this provider.");
        }

        var session = await _store.FindByShortCodeAsync(pending.ShortCode, ct);
        if (session is null)
        {
            return OAuthCompleteResult.Fail(OAuthStatus.SessionNotFound, "Session not found.");
        }

        OAuthConnection connection;
        try
        {
            connection = await flow.CompleteAsync(code, pending.CodeVerifier, redirectUri, ct);
        }
        catch (TrackerException ex)
        {
            return OAuthCompleteResult.Fail(OAuthStatus.Failed, ex.Message);
        }

        // Carry the optional manual story-points field through from the login request (#41).
        var resolved = pending.StoryPointsFieldOverride is { Length: > 0 } o
            ? connection.Connection with { StoryPointsFieldOverride = o }
            : connection.Connection;
        _connections.Set(session.Id, resolved, connection.AccountName);
        session.LinkedProvider = pending.Provider;
        session.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);

        return OAuthCompleteResult.Ok(session.ShortCode, pending.UserId, connection.AccountName, SessionService.ToSnapshot(session));
    }

    private async Task<(Models.Session? Session, OAuthStatus? Error)> LoadForControlAsync(
        string shortCode, string userId, CancellationToken ct)
    {
        var session = await _store.FindByShortCodeAsync(shortCode, ct);
        if (session is null)
        {
            return (null, OAuthStatus.SessionNotFound);
        }

        if (session.Participants.All(p => p.UserId != userId))
        {
            return (null, OAuthStatus.NotParticipant);
        }

        if (session.OrganiserUserId is not null && session.OrganiserUserId != userId)
        {
            return (null, OAuthStatus.NotOrganiser);
        }

        return (session, null);
    }
}
