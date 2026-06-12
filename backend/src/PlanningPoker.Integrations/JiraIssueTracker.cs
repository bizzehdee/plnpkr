using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Integrations;

/// <summary>
/// Jira Cloud REST v3 adapter. Auth is HTTP Basic with the account email + API token. Resolves the
/// story-points custom field (its id varies per site) and caches it. See #4/#35/#41.
/// </summary>
public sealed class JiraIssueTracker : IIssueTracker
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITrackerHostPolicy _hostPolicy;

    // Resolved story-points field id per base URL.
    private readonly ConcurrentDictionary<string, StoryPointsField?> _fieldCache = new();

    // Resolved "Acceptance Criteria" custom-field id per base URL (null = none on this site).
    private readonly ConcurrentDictionary<string, string?> _acceptanceFieldCache = new();

    private readonly IHtmlDescriptionSanitizer _sanitizer;

    public JiraIssueTracker(IHttpClientFactory httpClientFactory, ITrackerHostPolicy hostPolicy, IHtmlDescriptionSanitizer sanitizer)
    {
        _httpClientFactory = httpClientFactory;
        _hostPolicy = hostPolicy;
        _sanitizer = sanitizer;
    }

    public IntegrationProvider Provider => IntegrationProvider.Jira;

    private sealed record StoryPointsField(string Id, string Name);

    public async Task<TrackerValidation> ValidateAsync(TrackerConnection connection, CancellationToken cancellationToken = default)
    {
        _hostPolicy.Validate(connection.BaseUrl);
        using var client = CreateClient(connection);

        using var response = await client.GetAsync(Api(connection, "/rest/api/3/myself"), cancellationToken);
        await ThrowIfFailed(response);

        var me = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var name = me.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
        return new TrackerValidation(true, name, null);
    }

    public async Task<IssueDetails> GetIssueAsync(TrackerConnection connection, string issueKey, CancellationToken cancellationToken = default)
    {
        _hostPolicy.Validate(connection.BaseUrl);
        using var client = CreateClient(connection);

        var spField = await ResolveStoryPointsFieldAsync(connection, client, cancellationToken);
        var acFieldId = await ResolveAcceptanceCriteriaFieldAsync(connection, client, cancellationToken);

        // A field must be in the fields list for Jira to include its rendered HTML under renderedFields
        // (expand alone isn't enough).
        var fieldList = new List<string> { "summary", "description" };
        if (spField is not null) fieldList.Add(spField.Id);
        if (acFieldId is not null) fieldList.Add(acFieldId);
        var fields = string.Join(',', fieldList);

        // expand=renderedFields → Jira renders the ADF description (and AC field) to HTML for us; we sanitise it.
        using var response = await client.GetAsync(
            Api(connection, $"/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}?fields={fields}&expand=renderedFields"), cancellationToken);
        await ThrowIfFailed(response);

        var issue = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var f = issue.GetProperty("fields");
        issue.TryGetProperty("renderedFields", out var rendered);
        var hasRendered = rendered.ValueKind == JsonValueKind.Object;

        var title = f.TryGetProperty("summary", out var s) ? s.GetString() ?? issueKey : issueKey;

        string? description = hasRendered
            && rendered.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                ? _sanitizer.Sanitize(d.GetString())
                : null;

        // Append the "Acceptance Criteria" field if the site has one and the ticket populated it (#35).
        if (acFieldId is not null)
        {
            string? acHtml = hasRendered
                && rendered.TryGetProperty(acFieldId, out var acEl) && acEl.ValueKind == JsonValueKind.String
                    ? acEl.GetString()
                    : f.TryGetProperty(acFieldId, out var acRaw) && acRaw.ValueKind == JsonValueKind.String
                        ? acRaw.GetString()
                        : null;
            description = TrackerDescription.WithAcceptanceCriteria(description, _sanitizer.Sanitize(acHtml));
        }

        double? points = null;
        if (spField is not null && f.TryGetProperty(spField.Id, out var sp) && sp.ValueKind == JsonValueKind.Number)
        {
            points = sp.GetDouble();
        }

        // Human "browse" link uses the site URL (BrowseBaseUrl) when the API base is an OAuth gateway.
        var browseBase = (connection.BrowseBaseUrl ?? connection.BaseUrl).TrimEnd('/');
        var url = $"{browseBase}/browse/{Uri.EscapeDataString(issueKey)}";
        return new IssueDetails(issueKey, title, description, url, points, spField?.Id, spField?.Name);
    }

    public async Task SetStoryPointsAsync(TrackerConnection connection, string issueKey, double points, CancellationToken cancellationToken = default)
    {
        _hostPolicy.Validate(connection.BaseUrl);
        using var client = CreateClient(connection);

        var spField = await ResolveStoryPointsFieldAsync(connection, client, cancellationToken);
        if (spField is null)
        {
            throw new TrackerException(TrackerErrorKind.InvalidRequest, "This Jira site has no story-points field to update.");
        }

        var body = new Dictionary<string, object> { ["fields"] = new Dictionary<string, object> { [spField.Id] = points } };
        using var response = await client.PutAsync(
            Api(connection, $"/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}"),
            JsonContent.Create(body), cancellationToken);
        await ThrowIfFailed(response);
    }

    public async Task<IReadOnlyList<IssueSummary>> SearchAsync(TrackerConnection connection, IssueQuery query, int maxResults, CancellationToken cancellationToken = default)
    {
        _hostPolicy.Validate(connection.BaseUrl);
        using var client = CreateClient(connection);
        var spField = await ResolveStoryPointsFieldAsync(connection, client, cancellationToken);
        var fields = spField is null ? "summary,status" : $"summary,status,{spField.Id}";

        // Resolve the request URL for the query shape.
        string requestPath;
        switch (query)
        {
            // Jira Cloud removed the legacy /rest/api/3/search (now 410 Gone). The enhanced-JQL
            // endpoint /rest/api/3/search/jql replaces it; the "issues" array shape is unchanged.
            case JqlQuery jql:
                requestPath = $"/rest/api/3/search/jql?jql={Uri.EscapeDataString(jql.Jql)}&fields={fields}&maxResults={maxResults}";
                break;
            case KeyListQuery keys:
                var inClause = $"key in ({string.Join(',', keys.Keys)}) order by key";
                requestPath = $"/rest/api/3/search/jql?jql={Uri.EscapeDataString(inClause)}&fields={fields}&maxResults={maxResults}";
                break;
            case JiraBoardQuery board:
                requestPath = $"/rest/agile/1.0/board/{Uri.EscapeDataString(board.BoardId)}/issue?fields={fields}&maxResults={maxResults}";
                break;
            case JiraFilterQuery filter:
                var jqlFromFilter = await ResolveFilterJqlAsync(connection, filter.FilterId, client, cancellationToken);
                requestPath = $"/rest/api/3/search/jql?jql={Uri.EscapeDataString(jqlFromFilter)}&fields={fields}&maxResults={maxResults}";
                break;
            default:
                throw new TrackerException(TrackerErrorKind.InvalidRequest, "Unsupported query type for Jira.");
        }

        using var response = await client.GetAsync(Api(connection, requestPath), cancellationToken);
        await ThrowIfFailed(response);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var browseBase = (connection.BrowseBaseUrl ?? connection.BaseUrl).TrimEnd('/');
        var results = new List<IssueSummary>();
        if (doc.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
        {
            foreach (var issue in issues.EnumerateArray())
            {
                var key = issue.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                if (key.Length == 0) continue;
                var f = issue.GetProperty("fields");
                var title = f.TryGetProperty("summary", out var s) ? s.GetString() ?? key : key;
                string? status = f.TryGetProperty("status", out var st) && st.TryGetProperty("name", out var sn) ? sn.GetString() : null;
                double? points = spField is not null && f.TryGetProperty(spField.Id, out var sp) && sp.ValueKind == JsonValueKind.Number
                    ? sp.GetDouble() : null;
                results.Add(new IssueSummary(key, title, status, points, $"{browseBase}/browse/{Uri.EscapeDataString(key)}"));
            }
        }
        return results;
    }

    private async Task<string> ResolveFilterJqlAsync(TrackerConnection connection, string filterId, HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync(Api(connection, $"/rest/api/3/filter/{Uri.EscapeDataString(filterId)}"), ct);
        await ThrowIfFailed(response);
        var filter = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return filter.TryGetProperty("jql", out var jql) ? jql.GetString() ?? "" : "";
    }

    private async Task<StoryPointsField?> ResolveStoryPointsFieldAsync(
        TrackerConnection connection, HttpClient client, CancellationToken ct)
    {
        var hasOverride = connection.StoryPointsFieldOverride is { Length: > 0 };
        // The override is part of the cache identity so a re-connect with a different field is honoured.
        var cacheKey = hasOverride ? $"{connection.BaseUrl}::{connection.StoryPointsFieldOverride}" : connection.BaseUrl;
        if (_fieldCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        using var response = await client.GetAsync(Api(connection, "/rest/api/3/field"), ct);
        await ThrowIfFailed(response);

        var fields = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var match = hasOverride
            ? ResolveOverride(fields, connection.StoryPointsFieldOverride!)
            : ResolveByHeuristic(fields);

        _fieldCache[cacheKey] = match;
        return match;
    }

    /// <summary>Resolves the site's "Acceptance Criteria" custom-field id by name (cached). See #35.</summary>
    private async Task<string?> ResolveAcceptanceCriteriaFieldAsync(TrackerConnection connection, HttpClient client, CancellationToken ct)
    {
        if (_acceptanceFieldCache.TryGetValue(connection.BaseUrl, out var cached))
        {
            return cached;
        }

        using var response = await client.GetAsync(Api(connection, "/rest/api/3/field"), ct);
        await ThrowIfFailed(response);

        var fields = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        string? id = null;
        foreach (var field in fields.EnumerateArray())
        {
            var name = field.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is not null
                && name.Contains("acceptance criteria", StringComparison.OrdinalIgnoreCase)
                && field.TryGetProperty("id", out var idEl))
            {
                id = idEl.GetString();
                break;
            }
        }

        _acceptanceFieldCache[connection.BaseUrl] = id;
        return id;
    }

    /// <summary>Manual fallback (#41): match the user-supplied value against a field id or a field name.</summary>
    private static StoryPointsField? ResolveOverride(JsonElement fields, string overrideValue)
    {
        var wanted = overrideValue.Trim();
        foreach (var field in fields.EnumerateArray())
        {
            var id = field.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var name = field.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (id is not null && id.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return new StoryPointsField(id, name ?? wanted);
            }
            if (name is not null && id is not null && name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return new StoryPointsField(id, name);
            }
        }

        // Not in the field list but it looks like a Jira custom-field id — trust it verbatim.
        return wanted.StartsWith("customfield_", StringComparison.OrdinalIgnoreCase)
            ? new StoryPointsField(wanted, wanted)
            : null;
    }

    /// <summary>Auto-detection: a numeric field whose name contains "story point".</summary>
    private static StoryPointsField? ResolveByHeuristic(JsonElement fields)
    {
        StoryPointsField? match = null;
        foreach (var field in fields.EnumerateArray())
        {
            var name = field.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null || !name.Contains("story point", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isNumber = field.TryGetProperty("schema", out var schema)
                && schema.TryGetProperty("type", out var t)
                && t.GetString() == "number";
            if (!isNumber)
            {
                continue;
            }

            var id = field.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (id is null)
            {
                continue;
            }

            match = new StoryPointsField(id, name);
            // Prefer the team-managed "Story point estimate" if present; otherwise take the first match.
            if (name.Equals("Story point estimate", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return match;
    }

    /// <summary>Builds an API URL by appending to the base, preserving any path prefix (OAuth gateway base).</summary>
    private static Uri Api(TrackerConnection connection, string relativePath) =>
        new($"{connection.BaseUrl.TrimEnd('/')}{relativePath}");

    private HttpClient CreateClient(TrackerConnection connection)
    {
        var client = _httpClientFactory.CreateClient("tracker");
        client.DefaultRequestHeaders.Authorization = connection.Scheme == AuthScheme.Bearer
            ? new AuthenticationHeaderValue("Bearer", connection.Token)
            : new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.Email}:{connection.Token}")));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static async Task ThrowIfFailed(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => TrackerErrorKind.Unauthorized,
            HttpStatusCode.Forbidden => TrackerErrorKind.Forbidden,
            HttpStatusCode.NotFound => TrackerErrorKind.NotFound,
            HttpStatusCode.TooManyRequests => TrackerErrorKind.RateLimited,
            _ => TrackerErrorKind.Unavailable,
        };
        var message = kind switch
        {
            TrackerErrorKind.Unauthorized => "Jira rejected the credentials (401).",
            TrackerErrorKind.Forbidden => "The account lacks permission for this action (403).",
            TrackerErrorKind.NotFound => "The Jira issue was not found (404).",
            TrackerErrorKind.RateLimited => "Jira rate limit hit — try again shortly (429).",
            _ => $"Jira request failed ({(int)response.StatusCode}).",
        };
        throw new TrackerException(kind, message);
    }
}
