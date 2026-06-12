using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using Xunit;

namespace PlanningPoker.Api.Tests;

public class IntegrationOAuthEndpointTests : IClassFixture<IntegrationApiFactory>
{
    private readonly IntegrationApiFactory _factory;
    public IntegrationOAuthEndpointTests(IntegrationApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Options_lists_jira_as_enabled_with_oauth_configured()
    {
        var client = _factory.CreateClient();

        var options = await client.GetFromJsonAsync<JsonElement>("/api/integrations/options");

        var providers = options.GetProperty("providers").EnumerateArray().ToList();
        var jira = providers.SingleOrDefault(p => p.GetProperty("id").GetString() == "Jira");
        jira.ValueKind.Should().NotBe(JsonValueKind.Undefined); // Jira is enabled
        jira.GetProperty("oauth").GetBoolean().Should().BeTrue(); // and has OAuth configured (faked)
    }

    [Fact]
    public async Task Connect_redirects_the_organiser_to_the_provider_and_callback_completes_the_login()
    {
        // 1) Create a session (organiser = alice) over the hub.
        var server = _factory.Server;
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/poker"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .AddJsonProtocol(j => j.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        await hub.StartAsync();
        var created = await hub.InvokeAsync<CreateSessionResult>(
            "CreateSession", "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, (string?)null, true, (int?)null);
        var code = created.Session!.ShortCode;

        // 2) GET connect → 302 to the provider authorize URL (don't auto-follow).
        var noFollow = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var connect = await noFollow.GetAsync($"/api/integrations/jira/connect?session={code}&userId=alice");
        connect.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = connect.Headers.Location!.ToString();
        location.Should().StartWith("https://auth.example.com/authorize?state=");

        // 3) Simulate the provider redirecting back to the callback with that state + a code.
        var state = location[(location.IndexOf("state=", StringComparison.Ordinal) + "state=".Length)..].Split('&')[0];
        var callback = await noFollow.GetAsync($"/api/integrations/jira/callback?code=auth-code&state={state}");

        callback.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await callback.Content.ReadAsStringAsync();
        body.Should().Contain("pp-tracker-connected").And.Contain("Ada Lovelace");
    }

    [Fact]
    public async Task Callback_with_an_invalid_state_reports_an_error_to_the_opener()
    {
        var client = _factory.CreateClient();

        var callback = await client.GetAsync("/api/integrations/jira/callback?code=x&state=nope");
        var body = await callback.Content.ReadAsStringAsync();

        body.Should().Contain("pp-tracker-error");
    }
}
