using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using Xunit;

namespace TeamTools.Api.Tests;

/// <summary>
/// The retro export endpoint end to end (#28): runs a retro over the hub, then downloads it.
/// <para>
/// The route is a <b>POST</b>, which is the point of most of these tests. A protected board's
/// export needs its password, and a password in a query string ends up in server logs, proxy logs
/// and browser history — so it travels in the body instead.
/// </para>
/// </summary>
public class RetroExportEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public RetroExportEndpointTests(ApiFactory factory) => _factory = factory;

    private HubConnection Connect() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/retro"), o =>
            {
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(j => j.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

    /// <summary>Runs a retro to Discuss with one card and one action, and returns its short code.</summary>
    private async Task<string> RunRetroAsync(HubConnection hub, string? password = null)
    {
        var created = await hub.InvokeCoreAsync<CreateRetroResult>("CreateBoard", [
            "Sprint 24 retro", RetroTemplate.MadSadGlad, null, "u1", "Ann",
            true, password, true, false, null, null,
        ]);
        var code = created.Board!.Room.ShortCode;

        var columnId = created.Board.Columns[0].Id;
        await hub.InvokeAsync<RetroActionResult>("AddCard", code, "u1", columnId, "CI is slow");
        await hub.InvokeAsync<RetroActionResult>("AdvancePhase", code, "u1", (int?)null); // → Group
        await hub.InvokeAsync<RetroActionResult>("AdvancePhase", code, "u1", (int?)null); // → Vote
        await hub.InvokeAsync<RetroActionResult>("AdvancePhase", code, "u1", (int?)null); // → Discuss
        await hub.InvokeAsync<RetroActionResult>(
            "AddAction", code, "u1", "Speed up CI", null, "Ann", (DateTimeOffset?)null, (Guid?)null);

        return code;
    }

    private Task<HttpResponseMessage> ExportAsync(string code, string format, string? password = null) =>
        _factory.CreateClient().PostAsJsonAsync(
            $"/api/retro/{code}/export", new { format, password });

    [Fact]
    public async Task Export_downloads_the_board_as_markdown()
    {
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRetroAsync(hub);

        var response = await ExportAsync(code, "md");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/markdown");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain(code);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().StartWith("# Sprint 24 retro");
        body.Should().Contain("CI is slow");
        body.Should().Contain("- [ ] Speed up CI");
    }

    [Fact]
    public async Task Export_downloads_the_board_as_csv_and_json()
    {
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRetroAsync(hub);

        var csv = await ExportAsync(code, "csv");
        var json = await ExportAsync(code, "json");

        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await csv.Content.ReadAsStringAsync()).Should().StartWith("Kind,Theme,Column,Text,Author");

        json.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        // camelCase, because the summary view reads this payload directly.
        (await json.Content.ReadAsStringAsync()).Should().Contain("\"shortCode\"");
    }

    [Fact]
    public async Task Export_defaults_to_markdown()
    {
        // The format that actually gets pasted into a wiki page the morning after.
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRetroAsync(hub);

        var response = await _factory.CreateClient()
            .PostAsJsonAsync($"/api/retro/{code}/export", new { });

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/markdown");
    }

    [Fact]
    public async Task Export_returns_404_for_an_unknown_board()
    {
        var response = await ExportAsync("nope-nope", "md");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Export_returns_403_without_the_boards_password()
    {
        // 403 rather than 401: there is no authentication scheme here to challenge with.
        await using var hub = Connect();
        await hub.StartAsync();
        var code = await RunRetroAsync(hub, password: "hunter2");

        var refused = await ExportAsync(code, "md");
        var allowed = await ExportAsync(code, "md", "hunter2");

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Export_returns_409_before_the_discussion_starts()
    {
        // Export must not be a side door around hidden collection (#23) or the withheld dot totals
        // (#25).
        await using var hub = Connect();
        await hub.StartAsync();
        var created = await hub.InvokeCoreAsync<CreateRetroResult>("CreateBoard", [
            "Fresh retro", RetroTemplate.MadSadGlad, null, "u1", "Ann",
            true, null, true, false, null, null,
        ]);

        var response = await ExportAsync(created.Board!.Room.ShortCode, "md");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_anonymous_boards_export_names_nobody()
    {
        // The guarantee that matters (#22), checked at the edge of the system rather than only in
        // the renderer: whatever leaves over HTTP must not attribute an anonymous card.
        await using var hub = Connect();
        await hub.StartAsync();
        var created = await hub.InvokeCoreAsync<CreateRetroResult>("CreateBoard", [
            "Anonymous retro", RetroTemplate.MadSadGlad, null, "u1", "Ann",
            true, null, true, true, null, null,
        ]);
        var code = created.Board!.Room.ShortCode;
        await hub.InvokeAsync<RetroJoinResult>(
            "JoinBoard", code, "u2", "Bob", ParticipantRole.Voter, null);
        await hub.InvokeAsync<RetroActionResult>(
            "AddCard", code, "u2", created.Board.Columns[0].Id, "CI is slow");
        await hub.InvokeAsync<RetroActionResult>("AdvancePhase", code, "u1", (int?)null);
        await hub.InvokeAsync<RetroActionResult>("AdvancePhase", code, "u1", (int?)null);
        await hub.InvokeAsync<RetroActionResult>("AdvancePhase", code, "u1", (int?)null);

        foreach (var format in new[] { "md", "csv", "json" })
        {
            var body = await (await ExportAsync(code, format)).Content.ReadAsStringAsync();

            body.Should().Contain("CI is slow", $"the {format} export still carries the card");
            body.Should().NotContain("Bob", $"the {format} export must not name its author");
            body.Should().NotContain("u2", $"nor identify them by user id");
        }
    }
}
