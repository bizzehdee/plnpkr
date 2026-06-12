using System.Net;
using FluentAssertions;
using PlanningPoker.Core.Integrations;
using Xunit;

namespace PlanningPoker.Integrations.Tests;

public class AtlassianOAuthFlowTests
{
    private static readonly OAuthProviderConfig Config = new()
    {
        ClientId = "client-123",
        ClientSecret = "secret-xyz",
    };
    private const string Redirect = "https://app.example.com/api/integrations/jira/callback";

    private static AtlassianOAuthFlow Build(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), Config);

    [Fact]
    public void BuildAuthorizationUrl_includes_client_id_scopes_pkce_and_state()
    {
        var url = Build(new StubHttpMessageHandler()).BuildAuthorizationUrl(Redirect, "state-abc", "challenge-xyz");

        url.Should().StartWith("https://auth.atlassian.com/authorize?");
        url.Should().Contain("client_id=client-123");
        url.Should().Contain("response_type=code");
        url.Should().Contain("code_challenge=challenge-xyz");
        url.Should().Contain("code_challenge_method=S256");
        url.Should().Contain("state=state-abc");
        url.Should().Contain(Uri.EscapeDataString("read:jira-work"));
        url.Should().Contain(Uri.EscapeDataString(Redirect));
    }

    [Fact]
    public void IsConfigured_reflects_client_credentials()
    {
        new AtlassianOAuthFlow(new StubHttpClientFactory(new StubHttpMessageHandler()), new OAuthProviderConfig())
            .IsConfigured.Should().BeFalse();
        Build(new StubHttpMessageHandler()).IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteAsync_exchanges_the_code_and_resolves_a_bearer_connection()
    {
        var handler = new StubHttpMessageHandler()
            .Map("POST", "/oauth/token", HttpStatusCode.OK, """{ "access_token": "ACCESS-1", "refresh_token": "R" }""")
            .Map("GET", "/accessible-resources", HttpStatusCode.OK,
                """[ { "id": "cloud-1", "name": "Acme", "url": "https://acme.atlassian.net" } ]""");

        var result = await Build(handler).CompleteAsync("auth-code", "verifier", Redirect);

        result.AccountName.Should().Be("Acme");
        result.Connection.Scheme.Should().Be(AuthScheme.Bearer);
        result.Connection.Token.Should().Be("ACCESS-1");
        result.Connection.BaseUrl.Should().Be("https://api.atlassian.com/ex/jira/cloud-1");
        result.Connection.BrowseBaseUrl.Should().Be("https://acme.atlassian.net");
        // The token request carried the PKCE verifier and the auth code.
        handler.RequestBodies.Should().Contain(b => b.Contains("auth-code") && b.Contains("verifier"));
    }

    [Fact]
    public async Task CompleteAsync_throws_when_the_token_exchange_fails()
    {
        var handler = new StubHttpMessageHandler()
            .Map("POST", "/oauth/token", HttpStatusCode.BadRequest, null);

        var act = () => Build(handler).CompleteAsync("bad", "verifier", Redirect);

        (await act.Should().ThrowAsync<TrackerException>()).Which.Kind.Should().Be(TrackerErrorKind.Unauthorized);
    }
}
