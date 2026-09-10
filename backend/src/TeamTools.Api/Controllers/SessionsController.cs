using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using TeamTools.Core.Poker;
using TeamTools.Core;

namespace TeamTools.Api.Controllers;

/// <summary>The analytics request body. See <see cref="SessionsController.Analytics"/> for why it is a body.</summary>
public sealed class SessionAnalyticsRequest
{
    /// <summary>The session's join password, if it has one.</summary>
    public string? Password { get; set; }
}

/// <summary>The export request body: the analytics request plus a format.</summary>
public sealed class SessionExportRequest
{
    /// <summary>csv | json. Defaults to json, as the route did when it was a GET.</summary>
    public string Format { get; set; } = "json";

    /// <summary>The session's join password, if it has one.</summary>
    public string? Password { get; set; }
}

/// <summary>
/// Read-only REST surface used to validate/bootstrap an invite link before the client opens a
/// SignalR connection (the /join landing page), plus the poker round-history reads.
/// Thin adapter over <see cref="PokerService"/>. See #34/#6/#9/#11/#12/#30.
/// </summary>
[ApiController]
[Route("api/sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly PokerService _sessions;

    public SessionsController(PokerService sessions) => _sessions = sessions;

    /// <summary>
    /// Landing info for /join: name + whether a password is required (never the hash). See #2.
    /// <para>
    /// The one round-history-adjacent read that is <b>deliberately unguarded</b>: it is how the join
    /// page learns a password is needed at all, and it carries nothing but the name and that fact.
    /// </para>
    /// </summary>
    [HttpGet("{shortCode}")]
    public async Task<IActionResult> Get(string shortCode, CancellationToken ct)
    {
        var landing = await _sessions.GetLandingAsync(shortCode, ct);
        return landing is null ? NotFound() : Ok(landing);
    }

    /// <summary>
    /// Velocity/throughput analytics: per-round history + summary counts (#11).
    /// <para>
    /// <b>A POST, not a GET, since #30</b> — for the same reason the retro export is
    /// (<see cref="RetroController.Export"/>). A protected session's history needs its password, and
    /// a password in a query string ends up in server logs, proxy logs and browser history.
    /// </para>
    /// </summary>
    [HttpPost("{shortCode}/analytics")]
    public async Task<IActionResult> Analytics(
        string shortCode, [FromBody] SessionAnalyticsRequest request, CancellationToken ct = default)
    {
        var (status, analytics) = await _sessions.GetAnalyticsAsync(shortCode, request.Password, ct);

        return status switch
        {
            SessionExportStatus.SessionNotFound => NotFound(),
            SessionExportStatus.PasswordRequired => Refused(),
            _ => Ok(analytics),
        };
    }

    private static readonly JsonSerializerOptions ExportJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Export the session's completed-round history as a downloadable file (#12). format=csv|json
    /// (default json). Password-guarded and a POST since #30 — see <see cref="Analytics"/>.
    /// The per-IP rate limiter applies to the route, as it does to the whole REST surface (#3).
    /// </summary>
    [HttpPost("{shortCode}/export")]
    public async Task<IActionResult> Export(
        string shortCode, [FromBody] SessionExportRequest request, CancellationToken ct = default)
    {
        if (string.Equals(request.Format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var (csvStatus, csv) = await _sessions.GetAnalyticsCsvAsync(shortCode, request.Password, ct);
            return csvStatus switch
            {
                SessionExportStatus.SessionNotFound => NotFound(),
                SessionExportStatus.PasswordRequired => Refused(),
                _ => File(Encoding.UTF8.GetBytes(csv!), "text/csv", $"{shortCode}-rounds.csv"),
            };
        }

        var (status, analytics) = await _sessions.GetAnalyticsAsync(shortCode, request.Password, ct);
        return status switch
        {
            SessionExportStatus.SessionNotFound => NotFound(),
            SessionExportStatus.PasswordRequired => Refused(),
            _ => File(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(analytics, ExportJson)),
                "application/json",
                $"{shortCode}-rounds.json"),
        };
    }

    /// <summary>403, not 401: there is no authentication scheme to challenge with here.</summary>
    private IActionResult Refused() =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = "PasswordRequired" });
}
