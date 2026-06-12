using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PlanningPoker.Api.Health;

/// <summary>
/// Writes a compact JSON body for the health endpoints: overall status plus a per-check breakdown.
/// The framework already sets the HTTP status code (200 healthy/degraded, 503 unhealthy). See #3.
/// </summary>
public static class HealthResponse
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
            }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
