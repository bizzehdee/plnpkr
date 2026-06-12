using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Integrations;
using PlanningPoker.Core.Models;
using Xunit;

namespace PlanningPoker.Api.Tests;

/// <summary>Fake tracker so the integration hub round-trip is tested without any network.</summary>
file sealed class FakeTracker : IIssueTracker
{
    public IntegrationProvider Provider => IntegrationProvider.Jira;
    public Task<TrackerValidation> ValidateAsync(TrackerConnection c, CancellationToken ct = default) =>
        Task.FromResult(new TrackerValidation(true, "Ada Lovelace", null));
    public Task<IssueDetails> GetIssueAsync(TrackerConnection c, string key, CancellationToken ct = default) =>
        Task.FromResult(new IssueDetails(key, "Login page", "Build the login", $"https://acme.atlassian.net/browse/{key}", 3, "customfield_10016", "Story point estimate"));
    public Task SetStoryPointsAsync(TrackerConnection c, string issueKey, double points, CancellationToken ct = default) =>
        Task.CompletedTask;
    public Task<IReadOnlyList<IssueSummary>> SearchAsync(TrackerConnection c, IssueQuery query, int maxResults, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IssueSummary>>(new[]
        {
            new IssueSummary("PROJ-1", "First", "To Do", null, "https://acme.atlassian.net/browse/PROJ-1"),
            new IssueSummary("PROJ-2", "Second", "Doing", 5, "https://acme.atlassian.net/browse/PROJ-2"),
        });
}

/// <summary>Configured fake OAuth flow so the connect/callback endpoints work without a real IdP.</summary>
file sealed class FakeOAuthFlow : IOAuthFlow
{
    public IntegrationProvider Provider => IntegrationProvider.Jira;
    public bool IsConfigured => true;
    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge) =>
        $"https://auth.example.com/authorize?state={state}&code_challenge={codeChallenge}";
    public Task<OAuthConnection> CompleteAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct = default) =>
        Task.FromResult(new OAuthConnection(
            new TrackerConnection(IntegrationProvider.Jira, "https://api.atlassian.com/ex/jira/c1", null, "tok", AuthScheme.Bearer),
            "Ada Lovelace"));
}

/// <summary>Boots the API with integrations enabled and the tracker + OAuth flow faked.</summary>
public sealed class IntegrationApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Integrations:Jira:Enabled", "true");
        builder.UseSetting("Integrations:Ado:Enabled", "true");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(typeof(IIssueTracker));
            services.AddSingleton<IIssueTracker, FakeTracker>();
            services.RemoveAll(typeof(IOAuthFlow));
            services.AddSingleton<IOAuthFlow, FakeOAuthFlow>();
        });
    }
}

public class IntegrationHubTests : IClassFixture<IntegrationApiFactory>
{
    private readonly IntegrationApiFactory _factory;
    public IntegrationHubTests(IntegrationApiFactory factory) => _factory = factory;

    private HubConnection BuildHub()
    {
        var server = _factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/poker"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .AddJsonProtocol(j => j.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
    }

    [Fact]
    public async Task Connect_then_link_broadcasts_the_linked_issue_to_everyone()
    {
        await using var organiser = BuildHub();
        await using var watcher = BuildHub();
        await organiser.StartAsync();
        await watcher.StartAsync();

        var created = await organiser.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;
        await watcher.InvokeAsync<JoinResult>("JoinSession", code, "bob", "Bob", ParticipantRole.Voter, (string?)null);

        // The watcher should receive the broadcast carrying the linked issue.
        var linked = new TaskCompletionSource<SessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.On<SessionSnapshot>("SessionUpdated", s =>
        {
            if (s.Integration?.LinkedIssue is not null) linked.TrySetResult(s);
        });

        var connect = await organiser.InvokeAsync<IntegrationResult>(
            "ConnectTracker", code, "alice", IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok", (string?)null);
        connect.Status.Should().Be(IntegrationStatus.Ok);
        connect.AccountName.Should().Be("Ada Lovelace");

        var link = await organiser.InvokeAsync<IntegrationResult>("LinkIssue", code, "alice", "PROJ-7");
        link.Status.Should().Be(IntegrationStatus.Ok);

        var completed = await Task.WhenAny(linked.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(linked.Task, "the linked issue should broadcast");
        var snapshot = await linked.Task;
        snapshot.Integration!.Provider.Should().Be(IntegrationProvider.Jira);
        snapshot.Integration.LinkedIssue!.Title.Should().Be("Login page");
        snapshot.Integration.LinkedIssue.StoryPoints.Should().Be(3);
    }

    [Fact]
    public async Task Organiser_can_connect_link_and_submit_story_points()
    {
        await using var organiser = BuildHub();
        await organiser.StartAsync();

        var created = await organiser.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;

        await organiser.InvokeAsync<IntegrationResult>(
            "ConnectTracker", code, "alice", IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok", (string?)null);
        await organiser.InvokeAsync<IntegrationResult>("LinkIssue", code, "alice", "PROJ-7");

        var submit = await organiser.InvokeAsync<IntegrationResult>("SubmitStoryPoints", code, "alice", 8.0);

        submit.Status.Should().Be(IntegrationStatus.Ok);
    }

    [Fact]
    public async Task Load_queue_from_keys_populates_the_queue_for_the_session()
    {
        await using var organiser = BuildHub();
        await organiser.StartAsync();

        var created = await organiser.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;
        await organiser.InvokeAsync<IntegrationResult>(
            "ConnectTracker", code, "alice", IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok", (string?)null);

        var result = await organiser.InvokeAsync<IntegrationResult>(
            "LoadQueueFromKeys", code, "alice", new[] { "PROJ-1", "PROJ-2" });

        result.Status.Should().Be(IntegrationStatus.Ok);
        result.Session!.Integration!.Queue.Select(q => q.Key).Should().Equal("PROJ-1", "PROJ-2");
    }

    [Fact]
    public async Task Non_organiser_cannot_connect()
    {
        await using var organiser = BuildHub();
        await using var bob = BuildHub();
        await organiser.StartAsync();
        await bob.StartAsync();

        var created = await organiser.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;
        await bob.InvokeAsync<JoinResult>("JoinSession", code, "bob", "Bob", ParticipantRole.Voter, (string?)null);

        var connect = await bob.InvokeAsync<IntegrationResult>(
            "ConnectTracker", code, "bob", IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok", (string?)null);

        connect.Status.Should().Be(IntegrationStatus.NotOrganiser);
    }
}
