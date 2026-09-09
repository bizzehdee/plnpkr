using System.Net.Http.Json;
using TeamTools.Api.Controllers;
using FluentAssertions;
using Xunit;

namespace TeamTools.Api.Tests;

/// <summary>GET /api/config surfaces server-driven config the SPA needs at runtime (#15).</summary>
public class ConfigEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public ConfigEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Returns_the_configured_retention_windows()
    {
        var client = _factory.CreateClient();

        var config = await client.GetFromJsonAsync<AppConfigResponse>("/api/config");

        config.Should().NotBeNull();
        config!.Retention.ClosedRetentionMonths.Should().Be(12);
        config.Retention.SoftDeleteRetentionDays.Should().Be(30);
        config.Retention.IdleRetentionDays.Should().Be(30);
    }
}
