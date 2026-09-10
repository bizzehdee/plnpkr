using System.Text;
using Microsoft.AspNetCore.Mvc;
using TeamTools.Core.Retro;

namespace TeamTools.Api.Controllers;

/// <summary>The export request body. See <see cref="RetroController.Export"/> for why it is a body.</summary>
public sealed class RetroExportRequest
{
    /// <summary>md | csv | json. Defaults to md — the format that actually gets pasted somewhere.</summary>
    public string Format { get; set; } = "md";

    /// <summary>The board's join password, if it has one.</summary>
    public string? Password { get; set; }
}

/// <summary>
/// The retro tool's REST surface: exporting a finished board (#28). The /join landing read is
/// tool-agnostic and already served by <c>SessionsController</c>, so it is not duplicated here.
/// </summary>
[ApiController]
[Route("api/retro")]
public sealed class RetroController : ControllerBase
{
    private readonly RetroService _retro;

    public RetroController(RetroService retro) => _retro = retro;

    /// <summary>
    /// Exports a retro board as a downloadable file (#28) — Markdown, CSV or JSON.
    /// <para>
    /// <b>A POST, not a GET, on purpose.</b> A protected board's export needs its password, and a
    /// password in a query string ends up in server logs, proxy logs and browser history. The body
    /// keeps it out of all three. The cost is that the download has to be triggered from script
    /// rather than a plain link, which the SPA does anyway.
    /// </para>
    /// The per-IP rate limiter applies to the route, as it does to the whole REST surface (#3).
    /// </summary>
    [HttpPost("{shortCode}/export")]
    public async Task<IActionResult> Export(
        string shortCode, [FromBody] RetroExportRequest request, CancellationToken ct = default)
    {
        var (status, export) = await _retro.GetExportAsync(shortCode, request.Password, ct);

        switch (status)
        {
            case RetroExportStatus.BoardNotFound:
                return NotFound();
            case RetroExportStatus.PasswordRequired:
                // 403, not 401: there is no authentication scheme to challenge with here.
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "PasswordRequired" });
            case RetroExportStatus.NotYetVisible:
                return Conflict(new { error = "NotYetVisible" });
        }

        return request.Format?.ToLowerInvariant() switch
        {
            "csv" => Download(RetroExportRenderer.ToCsv(export!), "text/csv", $"{shortCode}-retro.csv"),
            "json" => Download(
                RetroExportRenderer.ToJson(export!), "application/json", $"{shortCode}-retro.json"),
            _ => Download(
                RetroExportRenderer.ToMarkdown(export!), "text/markdown", $"{shortCode}-retro.md"),
        };
    }

    private FileContentResult Download(string content, string contentType, string fileName) =>
        File(Encoding.UTF8.GetBytes(content), contentType, fileName);
}
