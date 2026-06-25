using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using FluentAssertions;
using Xunit;

namespace PlanningPoker.Api.Tests;

/// <summary>End-to-end export endpoint (#12): records a round over the hub, then downloads it.</summary>
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

    [Fact]
    public async Task Export_returns_404_for_an_unknown_session()
    {
        var client = _factory.CreateClient();

        var csv = await client.GetAsync("/api/sessions/nope-nope/export?format=csv");
        var json = await client.GetAsync("/api/sessions/nope-nope/export?format=json");

        csv.StatusCode.Should().Be(HttpStatusCode.NotFound);
        json.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Export_downloads_the_recorded_round_history_as_csv()
    {
        await using var hub = Connect();
        await hub.StartAsync();

        // Create as a voter (no organiser → anyone can reveal/reset), estimate one item, then reset to record it.
        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "u1", "Ann", false, null, true, (int?)null);
        var code = created.Session!.ShortCode;

        await hub.InvokeAsync<JoinResult>("JoinSession", code, "u2", "Bob", ParticipantRole.Voter, null);
        await hub.InvokeAsync<SessionActionResult>("CastVote", code, "u1", "5");
        await hub.InvokeAsync<SessionActionResult>("CastVote", code, "u2", "5");
        await hub.InvokeAsync<SessionActionResult>("RevealVotes", code, "u1");
        await hub.InvokeAsync<SessionActionResult>("ResetRound", code, "u1"); // records the round

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/sessions/{code}/export?format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain(code);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("RecordedAt,Story,FinalEstimate");
        body.Should().Contain(",5,"); // the consensus final estimate
    }
}
