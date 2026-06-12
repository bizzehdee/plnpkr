using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Integrations;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class OAuthServiceTests
{
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly FakeOAuthFlow _flow = new();
    private readonly InMemoryOAuthFlowStore _pending = new();
    private readonly InMemoryIntegrationConnectionStore _connections = new();
    private readonly SessionService _sessions;
    private readonly OAuthService _sut;

    private const string Code = "blue-fox-42";
    private const string Organiser = "alice";
    private const string Bob = "bob";
    private const string Redirect = "https://app.example.com/api/integrations/jira/callback";

    public OAuthServiceTests()
    {
        _sessions = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
        _sut = new OAuthService(_store, _flow, _pending, _connections, _clock, new IntegrationsOptions { Jira = new() { Enabled = true }, Ado = new() { Enabled = true } });
    }

    private async Task SeedAsync()
    {
        await _sessions.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, Organiser, "Alice", true));
        await _sessions.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
    }

    [Fact]
    public async Task Authorize_returns_a_provider_url_for_the_organiser()
    {
        await SeedAsync();

        var result = await _sut.AuthorizeAsync(Code, Organiser, IntegrationProvider.Jira, Redirect);

        result.Status.Should().Be(OAuthStatus.Ok);
        result.AuthorizationUrl.Should().StartWith("https://auth.example.com/authorize?state=");
    }

    [Fact]
    public async Task Authorize_is_rejected_for_a_non_organiser()
    {
        await SeedAsync();

        var result = await _sut.AuthorizeAsync(Code, Bob, IntegrationProvider.Jira, Redirect);

        result.Status.Should().Be(OAuthStatus.NotOrganiser);
    }

    [Fact]
    public async Task Authorize_reports_not_configured_when_the_flow_is_unconfigured()
    {
        await SeedAsync();
        _flow.IsConfigured = false;

        var result = await _sut.AuthorizeAsync(Code, Organiser, IntegrationProvider.Jira, Redirect);

        result.Status.Should().Be(OAuthStatus.NotConfigured);
    }

    [Fact]
    public async Task Complete_exchanges_the_code_and_stores_a_bearer_connection()
    {
        await SeedAsync();
        var authorize = await _sut.AuthorizeAsync(Code, Organiser, IntegrationProvider.Jira, Redirect);
        var state = ExtractState(authorize.AuthorizationUrl!);

        var result = await _sut.CompleteAsync(state, "auth-code", Redirect);

        result.Status.Should().Be(OAuthStatus.Ok);
        result.AccountName.Should().Be("Ada Lovelace");
        result.ShortCode.Should().Be(Code);
        result.Session!.Integration!.Provider.Should().Be(IntegrationProvider.Jira);
        var sessionId = (await _store.FindByShortCodeAsync(Code))!.Id;
        _connections.TryGet(sessionId, out var conn).Should().BeTrue();
        conn.Scheme.Should().Be(AuthScheme.Bearer);
        _flow.LastCode.Should().Be("auth-code");
    }

    [Fact]
    public async Task Complete_carries_the_story_points_field_override_onto_the_connection()
    {
        await SeedAsync();
        var authorize = await _sut.AuthorizeAsync(Code, Organiser, IntegrationProvider.Jira, Redirect, "customfield_10004");
        var state = ExtractState(authorize.AuthorizationUrl!);

        await _sut.CompleteAsync(state, "auth-code", Redirect);

        var sessionId = (await _store.FindByShortCodeAsync(Code))!.Id;
        _connections.TryGet(sessionId, out var conn).Should().BeTrue();
        conn.StoryPointsFieldOverride.Should().Be("customfield_10004");
    }

    [Fact]
    public async Task Complete_with_an_unknown_state_is_rejected()
    {
        var result = await _sut.CompleteAsync("not-a-real-state", "code", Redirect);

        result.Status.Should().Be(OAuthStatus.InvalidState);
    }

    [Fact]
    public async Task State_is_single_use()
    {
        await SeedAsync();
        var authorize = await _sut.AuthorizeAsync(Code, Organiser, IntegrationProvider.Jira, Redirect);
        var state = ExtractState(authorize.AuthorizationUrl!);

        (await _sut.CompleteAsync(state, "code", Redirect)).Status.Should().Be(OAuthStatus.Ok);
        (await _sut.CompleteAsync(state, "code", Redirect)).Status.Should().Be(OAuthStatus.InvalidState);
    }

    [Fact]
    public async Task Complete_surfaces_a_token_exchange_failure()
    {
        await SeedAsync();
        var authorize = await _sut.AuthorizeAsync(Code, Organiser, IntegrationProvider.Jira, Redirect);
        var state = ExtractState(authorize.AuthorizationUrl!);
        _flow.CompleteThrows = new TrackerException(TrackerErrorKind.Unauthorized, "token exchange failed");

        var result = await _sut.CompleteAsync(state, "code", Redirect);

        result.Status.Should().Be(OAuthStatus.Failed);
    }

    [Fact]
    public async Task Authorize_is_rejected_when_the_feature_is_disabled()
    {
        await SeedAsync();
        var disabled = new OAuthService(_store, _flow, _pending, _connections, _clock, new IntegrationsOptions { Jira = new() { Enabled = false }, Ado = new() { Enabled = false } });

        var result = await disabled.AuthorizeAsync(Code, Organiser, IntegrationProvider.Jira, Redirect);

        result.Status.Should().Be(OAuthStatus.Disabled);
    }

    [Fact]
    public async Task Authorize_unknown_session_returns_not_found()
    {
        var result = await _sut.AuthorizeAsync("missing-code-1", Organiser, IntegrationProvider.Jira, Redirect);

        result.Status.Should().Be(OAuthStatus.SessionNotFound);
    }

    [Fact]
    public async Task Authorize_by_a_non_participant_is_rejected()
    {
        await SeedAsync();

        var result = await _sut.AuthorizeAsync(Code, "stranger", IntegrationProvider.Jira, Redirect);

        result.Status.Should().Be(OAuthStatus.NotParticipant);
    }

    [Fact]
    public async Task Complete_is_rejected_when_the_feature_is_disabled()
    {
        var disabled = new OAuthService(_store, _flow, _pending, _connections, _clock, new IntegrationsOptions { Jira = new() { Enabled = false }, Ado = new() { Enabled = false } });
        // Mint a valid pending state directly (Authorize would be refused while disabled), so completion
        // gets past state validation and is rejected specifically because the feature is off.
        var state = _pending.Create(IntegrationProvider.Jira, Code, Organiser, "verifier", _clock.UtcNow, null);

        var result = await disabled.CompleteAsync(state, "code", Redirect);

        result.Status.Should().Be(OAuthStatus.Disabled);
    }

    [Fact]
    public async Task IsConfigured_reflects_the_flow()
    {
        _sut.IsConfigured(IntegrationProvider.Jira).Should().BeTrue();
        _sut.IsConfigured(IntegrationProvider.AzureDevOps).Should().BeFalse(); // fake flow only serves Jira
    }

    [Fact]
    public async Task Pkce_create_produces_distinct_verifier_and_challenge()
    {
        var (verifier, challenge) = Pkce.Create();

        verifier.Should().NotBeNullOrEmpty();
        challenge.Should().NotBeNullOrEmpty().And.NotBe(verifier);
    }

    private static string ExtractState(string url)
    {
        var query = new Uri(url).Query.TrimStart('?');
        var pair = query.Split('&').First(p => p.StartsWith("state=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(pair["state=".Length..]);
    }
}
