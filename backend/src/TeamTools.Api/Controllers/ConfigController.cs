using Microsoft.AspNetCore.Mvc;
using TeamTools.Core;

namespace TeamTools.Api.Controllers;

/// <summary>
/// Small, growable surface for server-driven config the SPA needs at runtime without a dedicated
/// endpoint per feature. Currently just the retention windows (#15), so the "Close or delete session"
/// modal can tell the organiser what each action actually leads to, instead of the frontend
/// hard-coding numbers that could drift from the server's configured <see cref="RetentionOptions"/>.
/// </summary>
[ApiController]
[Route("api/config")]
public sealed class ConfigController : ControllerBase
{
    private readonly RetentionOptions _retention;

    public ConfigController(RetentionOptions retention) => _retention = retention;

    [HttpGet]
    public IActionResult Get() => Ok(new AppConfigResponse(new RetentionConfig(
        _retention.ClosedRetentionMonths,
        _retention.SoftDeleteRetentionDays,
        _retention.IdleRetentionDays)));
}

public record AppConfigResponse(RetentionConfig Retention);

public record RetentionConfig(int ClosedRetentionMonths, int SoftDeleteRetentionDays, int IdleRetentionDays);
