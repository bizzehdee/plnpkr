using System.Net;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using Xunit;

namespace PlanningPoker.Api.Tests;

/// <summary>
/// End-to-end-ish coverage of the wiring: REST bootstrap endpoint and the SignalR hub broadcasting
/// to a session group. Business rules are covered in Core.Tests; these guard the transport. See #16.
/// </summary>
public class HubIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HubIntegrationTests(ApiFactory factory) => _factory = factory;

    private HubConnection BuildHub()
    {
        var server = _factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/poker"), options =>
            {
                // Drive SignalR through the in-memory TestServer handler over long polling.
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
    }

    [Fact]
    public async Task Rest_get_for_unknown_short_code_returns_404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sessions/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_reports_healthy_when_the_database_is_reachable()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"");
        body.Should().Contain("\"name\":\"database\""); // the DB readiness check ran
    }

    [Fact]
    public async Task Liveness_is_healthy_without_running_dependency_checks()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"");
        body.Should().NotContain("\"name\":\"database\""); // liveness excludes the DB check
    }

    [Fact]
    public async Task Created_session_is_retrievable_via_rest()
    {
        await using var hub = BuildHub();
        await hub.StartAsync();

        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        created.Status.Should().Be(CreateSessionStatus.Ok);

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/sessions/{created.Session!.ShortCode}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Joining_broadcasts_an_updated_snapshot_to_other_clients_in_the_session()
    {
        await using var organiser = BuildHub();
        await using var joiner = BuildHub();
        await organiser.StartAsync();
        await joiner.StartAsync();

        // The organiser listens for the broadcast that fires when someone joins.
        var broadcast = new TaskCompletionSource<SessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        organiser.On<SessionSnapshot>("SessionUpdated", s => broadcast.TrySetResult(s));

        var created = await organiser.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;

        var joinResult = await joiner.InvokeAsync<JoinResult>(
            "JoinSession", code, "bob", "Bob", ParticipantRole.Voter, (string?)null);
        joinResult.Status.Should().Be(JoinStatus.Ok);

        var snapshot = await WaitAsync(broadcast.Task);
        snapshot.Participants.Select(p => p.DisplayName).Should().Contain(new[] { "Alice", "Bob" });
    }

    [Fact]
    public async Task A_reaction_is_broadcast_to_the_session_group()
    {
        await using var organiser = BuildHub();
        await using var joiner = BuildHub();
        await organiser.StartAsync();
        await joiner.StartAsync();

        var received = new TaskCompletionSource<(string UserId, string Emoji)>(TaskCreationOptions.RunContinuationsAsynchronously);
        organiser.On<string, string>("ReactionReceived", (userId, emoji) => received.TrySetResult((userId, emoji)));

        var created = await organiser.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;
        await joiner.InvokeAsync<JoinResult>("JoinSession", code, "bob", "Bob", ParticipantRole.Voter, (string?)null);

        await joiner.InvokeAsync("React", "🎉");

        var (userId, emoji) = await WaitAsync(received.Task);
        userId.Should().Be("bob");
        emoji.Should().Be("🎉");
    }

    [Fact]
    public async Task A_disallowed_reaction_is_not_broadcast()
    {
        await using var hub = BuildHub();
        await hub.StartAsync();

        var fired = false;
        hub.On<string, string>("ReactionReceived", (_, _) => fired = true);

        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        created.Status.Should().Be(CreateSessionStatus.Ok);

        await hub.InvokeAsync("React", "💩"); // off the allowlist → ignored
        await Task.Delay(150);

        fired.Should().BeFalse();
    }

    [Fact]
    public async Task A_reaction_is_not_broadcast_when_reactions_are_disabled()
    {
        await using var hub = BuildHub();
        await hub.StartAsync();

        var fired = false;
        hub.On<string, string>("ReactionReceived", (_, _) => fired = true);

        // Create with reactions turned off — the server must refuse to fan out a reaction (#17).
        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, false, (int?)null);
        created.Status.Should().Be(CreateSessionStatus.Ok);

        await hub.InvokeAsync("React", "🎉"); // allowlisted, but reactions are disabled for this session
        await Task.Delay(150);

        fired.Should().BeFalse();
    }

    [Fact]
    public async Task Re_enabling_reactions_lets_them_broadcast_again()
    {
        await using var hub = BuildHub();
        await hub.StartAsync();

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<string, string>("ReactionReceived", (_, emoji) => received.TrySetResult(emoji));

        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, false, (int?)null);
        var code = created.Session!.ShortCode;

        // Organiser turns reactions back on, then a reaction goes through.
        var toggled = await hub.InvokeAsync<SessionActionResult>("SetReactionsEnabled", code, "alice", true);
        toggled.Status.Should().Be(SessionActionStatus.Ok);

        await hub.InvokeAsync("React", "🚀");

        (await WaitAsync(received.Task)).Should().Be("🚀");
    }

    [Fact]
    public async Task The_organiser_can_start_and_pause_the_round_timer_over_the_hub()
    {
        await using var hub = BuildHub();
        await hub.StartAsync();

        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;

        var started = await hub.InvokeAsync<SessionActionResult>("StartTimer", code, "alice", 60);
        started.Status.Should().Be(SessionActionStatus.Ok);
        started.Session!.TimerDurationSeconds.Should().Be(60);
        started.Session.TimerDeadline.Should().NotBeNull(); // running

        var paused = await hub.InvokeAsync<SessionActionResult>("PauseTimer", code, "alice");
        paused.Session!.TimerDeadline.Should().BeNull();
        paused.Session.TimerPausedRemainingSeconds.Should().NotBeNull(); // frozen
    }

    [Fact]
    public async Task The_round_timer_auto_reveals_when_it_expires()
    {
        await using var hub = BuildHub();
        await hub.StartAsync();

        var revealed = new TaskCompletionSource<SessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<SessionSnapshot>("SessionUpdated", s =>
        {
            if (s.State == SessionState.Revealed) revealed.TrySetResult(s);
        });

        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;

        // The minimum duration (5s) keeps the test short; the background RoundTimerService polls each
        // second and force-reveals once the deadline passes. See #14.
        await hub.InvokeAsync<SessionActionResult>("StartTimer", code, "alice", 5);

        var snapshot = await WaitAsync(revealed.Task);
        snapshot.State.Should().Be(SessionState.Revealed);
        snapshot.TimerDeadline.Should().BeNull();
    }

    [Fact]
    public async Task The_organiser_can_switch_the_deck_over_the_hub()
    {
        await using var hub = BuildHub();
        await hub.StartAsync();

        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;

        var switched = await hub.InvokeAsync<SessionActionResult>("SetDeck", code, "alice", DeckType.Custom, "1, 2, 4");

        switched.Status.Should().Be(SessionActionStatus.Ok);
        switched.Session!.DeckType.Should().Be(DeckType.Custom);
        switched.Session.Cards.Should().ContainInOrder("1", "2", "4");
    }

    [Fact]
    public async Task A_participant_can_switch_their_role_over_the_hub()
    {
        await using var organiser = BuildHub();
        await using var joiner = BuildHub();
        await organiser.StartAsync();
        await joiner.StartAsync();

        var created = await organiser.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;
        await joiner.InvokeAsync<JoinResult>("JoinSession", code, "bob", "Bob", ParticipantRole.Voter, (string?)null);

        // Bob (a voter) switches himself to observer; the broadcast snapshot reflects the new role.
        var result = await joiner.InvokeAsync<SessionActionResult>("ChangeRole", code, "bob", "bob", ParticipantRole.Observer);

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.Participants.Single(p => p.UserId == "bob").Role.Should().Be(ParticipantRole.Observer);
    }

    [Fact]
    public async Task Closing_makes_the_session_read_only_but_still_retrievable()
    {
        await using var hub = BuildHub();
        await hub.StartAsync();

        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", false, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;

        var closed = await hub.InvokeAsync<SessionActionResult>("CloseSession", code, "alice");
        closed.Status.Should().Be(SessionActionStatus.Ok);
        closed.Session!.IsClosed.Should().BeTrue();

        // A closed session rejects mutations…
        var vote = await hub.InvokeAsync<SessionActionResult>("CastVote", code, "alice", "5");
        vote.Status.Should().Be(SessionActionStatus.SessionClosed);

        // …but is still retrievable read-only over REST.
        var client = _factory.CreateClient();
        (await client.GetAsync($"/api/sessions/{code}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_hides_the_session_and_tells_clients_it_ended()
    {
        await using var hub = BuildHub();
        await hub.StartAsync();

        var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On("SessionClosed", () => ended.TrySetResult(true));

        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;

        var deleted = await hub.InvokeAsync<SessionActionResult>("DeleteSession", code, "alice");
        deleted.Status.Should().Be(SessionActionStatus.Ok);

        // The group is told the session ended…
        await WaitAsync(ended.Task);

        // …and it's now invisible: the REST GET 404s.
        var client = _factory.CreateClient();
        (await client.GetAsync($"/api/sessions/{code}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Observer_vote_is_rejected_over_the_hub()
    {
        await using var hub = BuildHub();
        await hub.StartAsync();

        // The organiser defaults to Observer and must not be able to vote.
        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);

        var vote = await hub.InvokeAsync<SessionActionResult>(
            "CastVote", created.Session!.ShortCode, "alice", "5");

        vote.Status.Should().Be(SessionActionStatus.ObserverCannotVote);
    }

    private static async Task<T> WaitAsync<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(task, "the broadcast should arrive within the timeout");
        return await task;
    }
}
