using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Integrations;

/// <summary>
/// Atlassian OAuth 2.0 (3LO) flow for Jira Cloud. Builds the authorize URL (with PKCE), exchanges the
/// code for an access token, resolves the accessible Jira site (cloud id), and returns a Bearer
/// connection whose API base is the Atlassian gateway. See #4.
/// </summary>
public sealed class AtlassianOAuthFlow : IOAuthFlow
{
    private const string AuthorizeUrl = "https://auth.atlassian.com/authorize";
    private const string TokenUrl = "https://auth.atlassian.com/oauth/token";
    private const string ResourcesUrl = "https://api.atlassian.com/oauth/token/accessible-resources";
    private const string DefaultScopes = "read:jira-work write:jira-work offline_access";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OAuthProviderConfig _config;

    public AtlassianOAuthFlow(IHttpClientFactory httpClientFactory, OAuthProviderConfig config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    public IntegrationProvider Provider => IntegrationProvider.Jira;
    public bool IsConfigured => _config.IsConfigured;

    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge)
    {
        var scopes = string.IsNullOrWhiteSpace(_config.Scopes) ? DefaultScopes : _config.Scopes!;
        var query = new Dictionary<string, string?>
        {
            ["audience"] = "api.atlassian.com",
            ["client_id"] = _config.ClientId,
            ["scope"] = scopes,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["response_type"] = "code",
            ["prompt"] = "consent",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        return $"{AuthorizeUrl}?{ToQueryString(query)}";
    }

    public async Task<OAuthConnection> CompleteAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("tracker");

        // 1) Exchange the authorization code for an access token.
        var tokenBody = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _config.ClientId!,
            ["client_secret"] = _config.ClientSecret!,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        };
        using var tokenResponse = await client.PostAsync(TokenUrl, new FormUrlEncodedContent(tokenBody), cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new TrackerException(TrackerErrorKind.Unauthorized, "Atlassian rejected the OAuth token exchange.");
        }
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var accessToken = token.GetProperty("access_token").GetString()
            ?? throw new TrackerException(TrackerErrorKind.Unauthorized, "No access token returned.");

        // 2) Find the Jira site (cloud id) this token can access.
        using var resourcesRequest = new HttpRequestMessage(HttpMethod.Get, ResourcesUrl);
        resourcesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        resourcesRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resourcesResponse = await client.SendAsync(resourcesRequest, cancellationToken);
        if (!resourcesResponse.IsSuccessStatusCode)
        {
            throw new TrackerException(TrackerErrorKind.Forbidden, "Could not list accessible Jira sites.");
        }
        var resources = await resourcesResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var site = resources.ValueKind == JsonValueKind.Array && resources.GetArrayLength() > 0
            ? resources[0]
            : throw new TrackerException(TrackerErrorKind.Forbidden, "This account has no accessible Jira sites.");

        var cloudId = site.GetProperty("id").GetString()!;
        var siteUrl = site.TryGetProperty("url", out var u) ? u.GetString() : null;

        var connection = new TrackerConnection(
            IntegrationProvider.Jira,
            $"https://api.atlassian.com/ex/jira/{cloudId}",
            Email: null,
            Token: accessToken,
            Scheme: AuthScheme.Bearer,
            BrowseBaseUrl: siteUrl);

        var accountName = site.TryGetProperty("name", out var n) ? n.GetString() : siteUrl;
        return new OAuthConnection(connection, accountName);
    }

    private static string ToQueryString(IDictionary<string, string?> values) =>
        string.Join('&', values
            .Where(kv => kv.Value is not null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));
}
