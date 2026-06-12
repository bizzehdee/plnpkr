using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Core.Tests.Fakes;

/// <summary>Fake OAuth flow + provider so OAuthService is tested without any provider/network.</summary>
public sealed class FakeOAuthFlow : IOAuthFlow, IOAuthFlowProvider
{
    public IntegrationProvider Provider { get; init; } = IntegrationProvider.Jira;
    public bool IsConfigured { get; set; } = true;
    public TrackerException? CompleteThrows { get; set; }
    public string? LastCode { get; private set; }
    public string? LastVerifier { get; private set; }

    public IOAuthFlow? For(IntegrationProvider provider) => provider == Provider ? this : null;

    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge) =>
        $"https://auth.example.com/authorize?state={state}&code_challenge={codeChallenge}&redirect_uri={Uri.EscapeDataString(redirectUri)}";

    public Task<OAuthConnection> CompleteAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
    {
        LastCode = code;
        LastVerifier = codeVerifier;
        if (CompleteThrows is not null) throw CompleteThrows;
        var conn = new TrackerConnection(Provider, "https://api.atlassian.com/ex/jira/cloud-1", null, "access-token", AuthScheme.Bearer);
        return Task.FromResult(new OAuthConnection(conn, "Ada Lovelace"));
    }
}
