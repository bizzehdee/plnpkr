using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using PlanningPoker.Core;

namespace PlanningPoker.Api.Controllers;

/// <summary>
/// Read-only REST surface used to validate/bootstrap an invite link before the client opens a
/// SignalR connection (the /join landing page). Thin adapter over <see cref="SessionService"/>.
/// See #34/#6/#9.
/// </summary>
[ApiController]
[Route("api/sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly SessionService _sessions;

    public SessionsController(SessionService sessions) => _sessions = sessions;

    /// <summary>Landing info for /join: name + whether a password is required (never the hash). See #2.</summary>
    [HttpGet("{shortCode}")]
    public async Task<IActionResult> Get(string shortCode, CancellationToken ct)
    {
        var landing = await _sessions.GetLandingAsync(shortCode, ct);
        return landing is null ? NotFound() : Ok(landing);
    }

    /// <summary>Velocity/throughput analytics: per-round history + summary counts (#11).</summary>
    [HttpGet("{shortCode}/analytics")]
    public async Task<IActionResult> Analytics(string shortCode, CancellationToken ct)
    {
        var analytics = await _sessions.GetAnalyticsAsync(shortCode, ct);
        return analytics is null ? NotFound() : Ok(analytics);
    }

    private static readonly JsonSerializerOptions ExportJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Export the session's completed-round history as a downloadable file (#12). format=csv|json
    /// (default json). Guarded by session existence; the per-IP rate limiter applies to the route.
    /// </summary>
    [HttpGet("{shortCode}/export")]
    public async Task<IActionResult> Export(string shortCode, [FromQuery] string format = "json", CancellationToken ct = default)
    {
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = await _sessions.GetAnalyticsCsvAsync(shortCode, ct);
            return csv is null
                ? NotFound()
                : File(Encoding.UTF8.GetBytes(csv), "text/csv", $"{shortCode}-rounds.csv");
        }

        var analytics = await _sessions.GetAnalyticsAsync(shortCode, ct);
        if (analytics is null)
        {
            return NotFound();
        }

        var json = JsonSerializer.Serialize(analytics, ExportJson);
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"{shortCode}-rounds.json");
    }
}
