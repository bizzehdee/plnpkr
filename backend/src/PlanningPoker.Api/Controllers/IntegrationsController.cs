using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PlanningPoker.Api.Hubs;
using PlanningPoker.Core;
using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Api.Controllers;

/// <summary>
/// OAuth "log in to the provider" REST surface (#4). The connect action redirects to the
/// provider; the callback exchanges the code, stores the connection for the session, broadcasts the
/// updated snapshot, and returns a tiny page that hands the result back to the opener window.
/// Thin adapter over <see cref="OAuthService"/>. See #9.
/// </summary>
[ApiController]
[Route("api/integrations")]
public sealed class IntegrationsController : ControllerBase
{
    private readonly OAuthService _oauth;
    private readonly IntegrationsOptions _options;

    public IntegrationsController(OAuthService oauth, IntegrationsOptions options)
    {
        _oauth = oauth;
        _options = options;
    }

    /// <summary>Lists only the enabled providers (each with whether it also has OAuth configured), so
    /// the UI shows just those — and nothing at all when neither is enabled. See #43.</summary>
    [HttpGet("options")]
    public IActionResult GetOptions()
    {
        var providers = new List<object>();
        if (_options.Jira.Enabled)
        {
            providers.Add(new { id = "Jira", oauth = _oauth.IsConfigured(IntegrationProvider.Jira) });
        }
        if (_options.Ado.Enabled)
        {
            providers.Add(new { id = "AzureDevOps", oauth = _oauth.IsConfigured(IntegrationProvider.AzureDevOps) });
        }
        return Ok(new { providers });
    }

    [HttpGet("{provider}/connect")]
    public async Task<IActionResult> Connect(
        string provider,
        [FromQuery] string session,
        [FromQuery] string userId,
        [FromQuery] string? spField)
    {
        if (!TryParseProvider(provider, out var p))
        {
            return BadRequest("Unknown provider.");
        }

        var redirectUri = CallbackUri(Request, provider);
        var result = await _oauth.AuthorizeAsync(session, userId, p, redirectUri, spField);
        return result.Status == OAuthStatus.Ok
            ? Redirect(result.AuthorizationUrl!)
            : BadRequest(result.Error);
    }

    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(
        string provider,
        [FromServices] IHubContext<PlanningPokerHub> hub,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return PopupResult(new { type = "pp-tracker-error", error = error ?? "Login was cancelled." });
        }

        var redirectUri = CallbackUri(Request, provider);
        var result = await _oauth.CompleteAsync(state, code, redirectUri);
        if (result.Status != OAuthStatus.Ok || result.Session is null)
        {
            return PopupResult(new { type = "pp-tracker-error", error = result.Error ?? "Login failed." });
        }

        await hub.Clients.Group(PlanningPokerHub.GroupName(result.ShortCode!))
            .SendAsync("SessionUpdated", result.Session);

        return PopupResult(new
        {
            type = "pp-tracker-connected",
            provider,
            shortCode = result.ShortCode,
            accountName = result.AccountName,
        });
    }

    private static bool TryParseProvider(string value, out IntegrationProvider provider)
    {
        switch (value.ToLowerInvariant())
        {
            case "jira": provider = IntegrationProvider.Jira; return true;
            case "azuredevops" or "ado": provider = IntegrationProvider.AzureDevOps; return true;
            default: provider = default; return false;
        }
    }

    private static string CallbackUri(HttpRequest request, string provider) =>
        $"{request.Scheme}://{request.Host}/api/integrations/{provider}/callback";

    /// <summary>
    /// Returns a minimal HTML page that posts the result to the opener window and closes itself.
    /// The message is JSON (default encoder escapes &lt; &gt; &amp;), so embedding it in the script is safe.
    /// </summary>
    private ContentResult PopupResult(object message)
    {
        var json = JsonSerializer.Serialize(message);
        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>Connecting…</title></head>
            <body style="font-family:sans-serif;padding:2rem">
            <p>You can close this window.</p>
            <script>
              (function () {
                var msg = {{json}};
                // Target '*' so it reaches the SPA opener even on a different dev-server origin;
                // the SPA validates event.origin against the API base before acting.
                if (window.opener) { window.opener.postMessage(msg, '*'); }
                window.close();
              })();
            </script>
            </body></html>
            """;
        return Content(html, "text/html");
    }
}
