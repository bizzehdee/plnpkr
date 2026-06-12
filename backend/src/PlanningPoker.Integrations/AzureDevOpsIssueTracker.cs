using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Integrations;

/// <summary>
/// Azure DevOps REST adapter. Auth is a PAT via HTTP Basic (empty username). The story-points field
/// defaults to the well-known <c>Microsoft.VSTS.Scheduling.StoryPoints</c>, but a manual reference
/// name or display name can be supplied as a fallback (#41). Base URL is the org, e.g.
/// https://dev.azure.com/{org}. Issue key is the numeric work-item id. See #4.
/// </summary>
public sealed partial class AzureDevOpsIssueTracker : IIssueTracker
{
    private const string DefaultStoryPointsField = "Microsoft.VSTS.Scheduling.StoryPoints";
    private const string AcceptanceCriteriaField = "Microsoft.VSTS.Common.AcceptanceCriteria";
    private const string ApiVersion = "api-version=7.1";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITrackerHostPolicy _hostPolicy;
    private readonly IHtmlDescriptionSanitizer _sanitizer;

    // Resolved story-points field per (baseUrl + override). Default field needs no resolution.
    private readonly ConcurrentDictionary<string, StoryPointsField> _fieldCache = new();

    private sealed record StoryPointsField(string ReferenceName, string DisplayName);

    public AzureDevOpsIssueTracker(IHttpClientFactory httpClientFactory, ITrackerHostPolicy hostPolicy, IHtmlDescriptionSanitizer sanitizer)
    {
        _httpClientFactory = httpClientFactory;
        _hostPolicy = hostPolicy;
        _sanitizer = sanitizer;
    }

    public IntegrationProvider Provider => IntegrationProvider.AzureDevOps;

    public async Task<TrackerValidation> ValidateAsync(TrackerConnection connection, CancellationToken cancellationToken = default)
    {
        var baseUri = _hostPolicy.Validate(connection.BaseUrl);
        using var client = CreateClient(connection);

        using var response = await client.GetAsync(new Uri(baseUri, $"_apis/connectionData?{ApiVersion}"), cancellationToken);
        await ThrowIfFailed(response);

        var data = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string? name = null;
        if (data.TryGetProperty("authenticatedUser", out var user))
        {
            name = user.TryGetProperty("providerDisplayName", out var pdn) ? pdn.GetString()
                : user.TryGetProperty("customDisplayName", out var cdn) ? cdn.GetString()
                : null;
        }
        return new TrackerValidation(true, name, null);
    }

    public async Task<IssueDetails> GetIssueAsync(TrackerConnection connection, string issueKey, CancellationToken cancellationToken = default)
    {
        var baseUri = _hostPolicy.Validate(connection.BaseUrl);
        using var client = CreateClient(connection);

        var spField = await ResolveStoryPointsFieldAsync(connection, client, baseUri, cancellationToken);
        var fields = $"System.Title,System.Description,{AcceptanceCriteriaField},{spField.ReferenceName}";
        using var response = await client.GetAsync(
            new Uri(baseUri, $"_apis/wit/workitems/{Uri.EscapeDataString(issueKey)}?fields={fields}&{ApiVersion}"),
            cancellationToken);
        await ThrowIfFailed(response);

        var item = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var f = item.GetProperty("fields");

        var title = f.TryGetProperty("System.Title", out var t) ? t.GetString() ?? issueKey : issueKey;
        // System.Description is already HTML — keep its formatting, just sanitise it.
        var description = f.TryGetProperty("System.Description", out var d) ? _sanitizer.Sanitize(d.GetString()) : null;

        // Acceptance Criteria is the well-known HTML field; append it under a subtitle when present (#35).
        if (f.TryGetProperty(AcceptanceCriteriaField, out var ac) && ac.ValueKind == JsonValueKind.String)
        {
            description = TrackerDescription.WithAcceptanceCriteria(description, _sanitizer.Sanitize(ac.GetString()));
        }

        double? points = null;
        if (f.TryGetProperty(spField.ReferenceName, out var sp) && sp.ValueKind == JsonValueKind.Number)
        {
            points = sp.GetDouble();
        }

        var url = TryGetHtmlLink(item) ?? new Uri(baseUri, $"_workitems/edit/{Uri.EscapeDataString(issueKey)}").ToString();
        // Story Points is a standard field on estimable work-item types; treat it as available.
        return new IssueDetails(issueKey, title, description, url, points, spField.ReferenceName, spField.DisplayName);
    }

    public async Task SetStoryPointsAsync(TrackerConnection connection, string issueKey, double points, CancellationToken cancellationToken = default)
    {
        var baseUri = _hostPolicy.Validate(connection.BaseUrl);
        using var client = CreateClient(connection);

        var spField = await ResolveStoryPointsFieldAsync(connection, client, baseUri, cancellationToken);
        var patch = new[]
        {
            new { op = "add", path = $"/fields/{spField.ReferenceName}", value = points },
        };
        var json = JsonSerializer.Serialize(patch);
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            new Uri(baseUri, $"_apis/wit/workitems/{Uri.EscapeDataString(issueKey)}?{ApiVersion}"))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json-patch+json"),
        };
        using var response = await client.SendAsync(request, cancellationToken);
        await ThrowIfFailed(response);
    }

    public async Task<IReadOnlyList<IssueSummary>> SearchAsync(TrackerConnection connection, IssueQuery query, int maxResults, CancellationToken cancellationToken = default)
    {
        var baseUri = _hostPolicy.Validate(connection.BaseUrl);
        using var client = CreateClient(connection);
        var spField = await ResolveStoryPointsFieldAsync(connection, client, baseUri, cancellationToken);

        // Resolve the list of work-item ids for the query, then batch-fetch their fields.
        IReadOnlyList<string> ids;
        switch (query)
        {
            case KeyListQuery keys:
                ids = keys.Keys.Take(maxResults).ToArray();
                break;
            case AdoSharedQuery q:
                using (var wiqlResponse = await client.GetAsync(
                    new Uri(baseUri, $"{Uri.EscapeDataString(q.Project)}/_apis/wit/wiql/{Uri.EscapeDataString(q.QueryId)}?{ApiVersion}"),
                    cancellationToken))
                {
                    await ThrowIfFailed(wiqlResponse);
                    var wiql = await wiqlResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                    ids = wiql.TryGetProperty("workItems", out var items) && items.ValueKind == JsonValueKind.Array
                        ? items.EnumerateArray().Select(w => w.GetProperty("id").GetRawText()).Take(maxResults).ToArray()
                        : Array.Empty<string>();
                }
                break;
            default:
                throw new TrackerException(TrackerErrorKind.InvalidRequest, "Unsupported query type for Azure DevOps.");
        }

        if (ids.Count == 0)
        {
            return Array.Empty<IssueSummary>();
        }

        var idList = string.Join(',', ids);
        var fields = $"System.Title,System.State,{spField.ReferenceName}";
        using var batch = await client.GetAsync(
            new Uri(baseUri, $"_apis/wit/workitems?ids={idList}&fields={fields}&{ApiVersion}"), cancellationToken);
        await ThrowIfFailed(batch);
        var doc = await batch.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var results = new List<IssueSummary>();
        if (doc.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetRawText() : "";
                if (id.Length == 0) continue;
                var f = item.GetProperty("fields");
                var title = f.TryGetProperty("System.Title", out var t) ? t.GetString() ?? id : id;
                string? status = f.TryGetProperty("System.State", out var st) ? st.GetString() : null;
                double? points = f.TryGetProperty(spField.ReferenceName, out var sp) && sp.ValueKind == JsonValueKind.Number
                    ? sp.GetDouble() : null;
                var url = TryGetHtmlLink(item) ?? new Uri(baseUri, $"_workitems/edit/{id}").ToString();
                results.Add(new IssueSummary(id, title, status, points, url));
            }
        }
        return results;
    }

    /// <summary>
    /// Resolves the story-points field. With no override, uses the well-known field (no network call).
    /// With an override (#41), looks it up in the org field list so either a reference name
    /// (e.g. <c>Custom.StoryPoints</c>) or a display name (e.g. "Story Points") works; if the list
    /// doesn't contain it, the override is trusted verbatim as a reference name.
    /// </summary>
    private async Task<StoryPointsField> ResolveStoryPointsFieldAsync(
        TrackerConnection connection, HttpClient client, Uri baseUri, CancellationToken ct)
    {
        if (connection.StoryPointsFieldOverride is not { Length: > 0 } wanted)
        {
            return new StoryPointsField(DefaultStoryPointsField, "Story Points");
        }

        wanted = wanted.Trim();
        var cacheKey = $"{connection.BaseUrl}::{wanted}";
        if (_fieldCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        StoryPointsField resolved = new(wanted, wanted); // verbatim fallback
        using var response = await client.GetAsync(new Uri(baseUri, $"_apis/wit/fields?{ApiVersion}"), ct);
        await ThrowIfFailed(response);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (doc.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in value.EnumerateArray())
            {
                var refName = field.TryGetProperty("referenceName", out var rn) ? rn.GetString() : null;
                var name = field.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                if ((refName is not null && refName.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    || (name is not null && name.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
                {
                    resolved = new StoryPointsField(refName ?? wanted, name ?? wanted);
                    break;
                }
            }
        }

        _fieldCache[cacheKey] = resolved;
        return resolved;
    }

    private HttpClient CreateClient(TrackerConnection connection)
    {
        var client = _httpClientFactory.CreateClient("tracker");
        // Azure DevOps PAT: HTTP Basic with an empty username.
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{connection.Token}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string? TryGetHtmlLink(JsonElement item) =>
        item.TryGetProperty("_links", out var links)
        && links.TryGetProperty("html", out var html)
        && html.TryGetProperty("href", out var href)
            ? href.GetString()
            : null;

    private static async Task ThrowIfFailed(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => TrackerErrorKind.Unauthorized,
            HttpStatusCode.NonAuthoritativeInformation => TrackerErrorKind.Unauthorized, // ADO often 203s an HTML sign-in page for bad PATs
            HttpStatusCode.Forbidden => TrackerErrorKind.Forbidden,
            HttpStatusCode.NotFound => TrackerErrorKind.NotFound,
            HttpStatusCode.TooManyRequests => TrackerErrorKind.RateLimited,
            _ => TrackerErrorKind.Unavailable,
        };
        var message = kind switch
        {
            TrackerErrorKind.Unauthorized => "Azure DevOps rejected the personal access token.",
            TrackerErrorKind.Forbidden => "The token lacks permission for this action.",
            TrackerErrorKind.NotFound => "The work item was not found.",
            TrackerErrorKind.RateLimited => "Azure DevOps rate limit hit — try again shortly.",
            _ => $"Azure DevOps request failed ({(int)response.StatusCode}).",
        };
        await Task.CompletedTask;
        throw new TrackerException(kind, message);
    }
}
