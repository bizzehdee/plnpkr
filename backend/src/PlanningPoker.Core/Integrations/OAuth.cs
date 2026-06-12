using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PlanningPoker.Core.Integrations;

/// <summary>Per-provider OAuth client configuration (from app config; secret never logged). See #4.</summary>
public sealed class OAuthProviderConfig
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scopes { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Outcome of a completed OAuth flow: a usable connection plus the authenticated account name.</summary>
public record OAuthConnection(TrackerConnection Connection, string? AccountName);

/// <summary>
/// A provider's OAuth (3LO) flow. Building the authorization URL is pure; completing it exchanges the
/// code for a token over HTTP. Implementations live in the Integrations adapter project. See #4.
/// </summary>
public interface IOAuthFlow
{
    IntegrationProvider Provider { get; }
    bool IsConfigured { get; }
    string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge);
    Task<OAuthConnection> CompleteAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default);
}

/// <summary>Resolves the <see cref="IOAuthFlow"/> for a provider (null if none registered/configured).</summary>
public interface IOAuthFlowProvider
{
    IOAuthFlow? For(IntegrationProvider provider);
}

/// <summary>The pending authorization started by the connect endpoint, looked up on callback.</summary>
public record PendingOAuth(
    IntegrationProvider Provider,
    string ShortCode,
    string UserId,
    string CodeVerifier,
    DateTimeOffset CreatedAt,
    string? StoryPointsFieldOverride = null);

/// <summary>
/// Holds in-flight OAuth authorizations keyed by an unguessable single-use <c>state</c>. Keeps the
/// PKCE code verifier server-side (never in the redirect URL) and expires stale entries. See #4.
/// </summary>
public interface IOAuthFlowStore
{
    string Create(IntegrationProvider provider, string shortCode, string userId, string codeVerifier, DateTimeOffset now,
        string? storyPointsFieldOverride = null);
    bool TryConsume(string state, DateTimeOffset now, out PendingOAuth pending);
}

public sealed class InMemoryOAuthFlowStore : IOAuthFlowStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, PendingOAuth> _pending = new();

    public string Create(IntegrationProvider provider, string shortCode, string userId, string codeVerifier, DateTimeOffset now,
        string? storyPointsFieldOverride = null)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        _pending[state] = new PendingOAuth(provider, shortCode, userId, codeVerifier, now, storyPointsFieldOverride);
        return state;
    }

    public bool TryConsume(string state, DateTimeOffset now, out PendingOAuth pending)
    {
        if (_pending.TryRemove(state, out pending!) && now - pending.CreatedAt <= Ttl)
        {
            return true;
        }

        pending = null!;
        return false;
    }
}

/// <summary>PKCE (RFC 7636) helper: a random verifier and its S256 challenge.</summary>
public static class Pkce
{
    public static (string Verifier, string Challenge) Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
