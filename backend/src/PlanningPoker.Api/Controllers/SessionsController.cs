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
}
