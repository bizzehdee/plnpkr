using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using FluentAssertions;
using Xunit;

namespace TeamTools.Api.Tests;

/// <summary>
/// End-to-end poker round-history endpoints (#11/#12): records a round over the hub, then reads and
/// downloads it.
/// <para>
/// Both routes are <b>POSTs</b> since #30, for the reason the retro export already was one
/// (<see cref="RetroExportEndpointTests"/>): a protected session's history needs its password, and a
/// password in a query string ends up in server logs, proxy logs and browser history.
/// </para>
/// </summary>
public class ExportEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public ExportEndpointTests(ApiFactory factory) => _factory = factory;

    private HubConnection Connect() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/poker"), o =>
            {
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(j => j.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

    /// <summary>
    /// Estimates one item and records it. Created as a voter (no organiser → anyone can reveal/reset).
    /// </summary>
    private async Task<string> RunRoundAsync(HubConnection hub, string? password = null)
    {
        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "u1", "Ann", false, password, true,
            (int?)null);
        var code = created.Session!.ShortCode;

        await hub.InvokeAsync<JoinResult>("JoinSession", code, "u2", "Bob", ParticipantRole.Voter, password);
        await hub.InvokeAsync<SessionActionResult>("CastVote", code, "u1", "5");
        await hub.InvokeAsync<SessionActionResult>("CastVote", code, "u2", "5");
        await hub.InvokeAsync<SessionActionResult>("RevealVotes", code, "u1");
        await hub.InvokeAsync<SessionActionResult>("ResetRound", code, "u1"); // records the round

        return code;
    }

    private Task<HttpResponseMessage> ExportAsync(string code, string format, string? password = null) =>
        _factory.CreateClient().PostAsJsonAsync($"/api/sessions/{code}/export", new { format, password });

    private Task<HttpResponseMessage> AnalyticsAsync(string code, string? password = null) =>
        _factory.CreateClient().PostAsJsonAsync($"/api/sessions/{code}/analytics", new { password });

    // --- Reads and downloads ----------------------------------------------

    [Fact]
    public async Task Export_downloads_the_recorded_round_history_as_csv()
    {
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRoundAsync(hub);

        var response = await ExportAsync(code, "csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain(code);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("RecordedAt,Story,FinalEstimate");
        body.Should().Contain(",5,"); // the consensus final estimate
    }

    [Fact]
    public async Task Export_defaults_to_json()
    {
        // The default the route had when it was a GET with ?format=.
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRoundAsync(hub);

        var response = await _factory.CreateClient()
            .PostAsJsonAsync($"/api/sessions/{code}/export", new { });

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain(code);
    }

    [Fact]
    public async Task Analytics_returns_the_summary_and_the_round_history()
    {
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRoundAsync(hub);

        var response = await AnalyticsAsync(code);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var analytics = await response.Content.ReadFromJsonAsync<SessionAnalytics>();
        analytics!.RoundsCompleted.Should().Be(1);
        analytics.ConsensusRounds.Should().Be(1);
    }

    [Fact]
    public async Task Both_routes_return_404_for_an_unknown_session()
    {
        var csv = await ExportAsync("nope-nope", "csv");
        var json = await ExportAsync("nope-nope", "json");
        var analytics = await AnalyticsAsync("nope-nope");

        csv.StatusCode.Should().Be(HttpStatusCode.NotFound);
        json.StatusCode.Should().Be(HttpStatusCode.NotFound);
        analytics.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- The password guard (#30) -----------------------------------------

    [Fact]
    public async Task Exporting_a_protected_session_returns_403_without_its_password()
    {
        // 403 rather than 401: there is no authentication scheme here to challenge with.
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRoundAsync(hub, password: "hunter2");

        var missing = await ExportAsync(code, "csv");
        var wrong = await ExportAsync(code, "csv", "guess");
        var right = await ExportAsync(code, "csv", "hunter2");

        missing.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        right.StatusCode.Should().Be(HttpStatusCode.OK);
        (await right.Content.ReadAsStringAsync()).Should().Contain("RecordedAt,Story");
    }

    [Fact]
    public async Task The_json_export_is_under_the_same_guard()
    {
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRoundAsync(hub, password: "hunter2");

        var missing = await ExportAsync(code, "json");
        var right = await ExportAsync(code, "json", "hunter2");

        missing.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        right.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Analytics_is_under_the_same_guard()
    {
        // It returns a superset of the export: guarding one and not the other would be theatre.
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRoundAsync(hub, password: "hunter2");

        var missing = await AnalyticsAsync(code);
        var right = await AnalyticsAsync(code, "hunter2");

        missing.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        right.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_session_without_a_password_is_unaffected()
    {
        // The guard must not turn every unprotected session into a locked one.
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRoundAsync(hub);

        var noPassword = await ExportAsync(code, "csv");
        var stray = await ExportAsync(code, "csv", "irrelevant");

        noPassword.StatusCode.Should().Be(HttpStatusCode.OK);
        stray.StatusCode.Should().Be(HttpStatusCode.OK, "a password nobody asked for is ignored");
    }

    [Fact]
    public async Task The_join_landing_read_stays_open_on_a_protected_session()
    {
        // It is how the join page learns a password is needed, and it carries only the name and that
        // fact — so it is deliberately outside the guard, and still a GET.
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRoundAsync(hub, password: "hunter2");

        var response = await _factory.CreateClient().GetAsync($"/api/sessions/{code}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var landing = await response.Content.ReadFromJsonAsync<SessionLanding>(LandingJson);
        landing!.RequiresPassword.Should().BeTrue();
        landing.Name.Should().Be("Sprint");
    }

    /// <summary>The API writes enums as names; a default reader would choke on <c>"Poker"</c>.</summary>
    private static readonly JsonSerializerOptions LandingJson =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task The_password_never_travels_in_the_url()
    {
        // The whole reason these routes are POSTs. A query string lands in server logs, proxy logs
        // and browser history, so there is deliberately no GET left to smuggle a password through.
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRoundAsync(hub, password: "hunter2");
        var client = _factory.CreateClient();

        var asQuery = await client.GetAsync($"/api/sessions/{code}/export?format=csv&password=hunter2");

        asQuery.IsSuccessStatusCode.Should().BeFalse();
        (await asQuery.Content.ReadAsStringAsync()).Should().NotContain("RecordedAt");
    }
}
