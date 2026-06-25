using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Integrations;

/// <summary>
/// GitLab Issues REST (v4) adapter (#14). The connection BaseUrl is the project web URL
/// (https://gitlab.com/{group}/{project}); the host policy validates it and the API base is derived
/// from the same scheme+host (so self-hosted GitLab on an allowlisted host also works). Auth is a PAT
/// via the PRIVATE-TOKEN header. Story points map to GitLab's native integer <c>weight</c>; a
/// fractional value falls back to a "points:" label (overridable via the story-points field, #41).
/// Issue "key" is the issue iid.
/// </summary>
public sealed class GitLabIssueTracker : IIssueTracker
{
    private const string DefaultPointsLabelPrefix = "points:";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITrackerHostPolicy _hostPolicy;
    private readonly IHtmlDescriptionSanitizer _sanitizer;

    public GitLabIssueTracker(IHttpClientFactory httpClientFactory, ITrackerHostPolicy hostPolicy, IHtmlDescriptionSanitizer sanitizer)
    {
        _httpClientFactory = httpClientFactory;
        _hostPolicy = hostPolicy;
        _sanitizer = sanitizer;
    }

    public IntegrationProvider Provider => IntegrationProvider.GitLab;

    public async Task<TrackerValidation> ValidateAsync(TrackerConnection connection, CancellationToken cancellationToken = default)
    {
        var apiBase = ApiBase(connection); // also validates the host
        using var client = CreateClient(connection);

        using var response = await client.GetAsync(new Uri($"{apiBase}/user"), cancellationToken);
        await ThrowIfFailed(response);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var name = user.TryGetProperty("name", out var n) ? n.GetString()
            : user.TryGetProperty("username", out var u) ? u.GetString() : null;
        return new TrackerValidation(true, name, null);
    }

    public async Task<IssueDetails> GetIssueAsync(TrackerConnection connection, string issueKey, CancellationToken cancellationToken = default)
    {
        var (apiBase, projectPath) = ApiAndProject(connection);
        using var client = CreateClient(connection);
        var iid = Iid(issueKey);

        using var response = await client.GetAsync(new Uri($"{apiBase}/projects/{projectPath}/issues/{iid}"), cancellationToken);
        await ThrowIfFailed(response);
        var issue = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var title = issue.TryGetProperty("title", out var t) ? t.GetString() ?? issueKey : issueKey;
        var description = issue.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? _sanitizer.Sanitize(d.GetString())
            : null;
        var url = issue.TryGetProperty("web_url", out var w) ? w.GetString() ?? "" : "";
        var prefix = PointsPrefix(connection);
        var points = WeightOrLabelPoints(issue, prefix);

        // weight is a standard issue field; treat points as available.
        return new IssueDetails(iid.ToString(CultureInfo.InvariantCulture), title, description, url, points, "weight", "Weight");
    }

    public async Task SetStoryPointsAsync(TrackerConnection connection, string issueKey, double points, CancellationToken cancellationToken = default)
    {
        var (apiBase, projectPath) = ApiAndProject(connection);
        using var client = CreateClient(connection);
        var iid = Iid(issueKey);

        // Whole numbers use the native integer weight; fractional values fall back to a points label.
        if (points == Math.Floor(points) && points >= 0)
        {
            var weight = (long)points;
            using var response = await client.PutAsync(
                new Uri($"{apiBase}/projects/{projectPath}/issues/{iid}?weight={weight}"), content: null, cancellationToken);
            await ThrowIfFailed(response);
            return;
        }

        var prefix = PointsPrefix(connection);
        var label = Uri.EscapeDataString($"{prefix} {points.ToString(CultureInfo.InvariantCulture)}");
        using var labelResponse = await client.PutAsync(
            new Uri($"{apiBase}/projects/{projectPath}/issues/{iid}?add_labels={label}"), content: null, cancellationToken);
        await ThrowIfFailed(labelResponse);
    }

    public async Task<IReadOnlyList<IssueSummary>> SearchAsync(TrackerConnection connection, IssueQuery query, int maxResults, CancellationToken cancellationToken = default)
    {
        var (apiBase, projectPath) = ApiAndProject(connection);
        using var client = CreateClient(connection);
        var prefix = PointsPrefix(connection);

        return query switch
        {
            KeyListQuery keys => await FetchEachAsync(client, apiBase, projectPath, keys.Keys.Take(maxResults), prefix, cancellationToken),
            OpenIssuesQuery => await ListOpenAsync(client, apiBase, projectPath, prefix, maxResults, cancellationToken),
            _ => throw new TrackerException(TrackerErrorKind.InvalidRequest, "Unsupported query type for GitLab."),
        };
    }

    private async Task<IReadOnlyList<IssueSummary>> FetchEachAsync(
        HttpClient client, string apiBase, string projectPath, IEnumerable<string> keys, string prefix, CancellationToken ct)
    {
        var results = new List<IssueSummary>();
        foreach (var key in keys)
        {
            var iid = Iid(key);
            using var response = await client.GetAsync(new Uri($"{apiBase}/projects/{projectPath}/issues/{iid}"), ct);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }
            var issue = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            results.Add(ToSummary(issue, iid, prefix));
        }
        return results;
    }

    private async Task<IReadOnlyList<IssueSummary>> ListOpenAsync(
        HttpClient client, string apiBase, string projectPath, string prefix, int maxResults, CancellationToken ct)
    {
        var perPage = Math.Clamp(maxResults, 1, 100);
        using var response = await client.GetAsync(
            new Uri($"{apiBase}/projects/{projectPath}/issues?state=opened&per_page={perPage}"), ct);
        await ThrowIfFailed(response);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        var results = new List<IssueSummary>();
        if (doc.ValueKind == JsonValueKind.Array)
        {
            foreach (var issue in doc.EnumerateArray())
            {
                var iid = issue.TryGetProperty("iid", out var i) ? i.GetInt32() : 0;
                if (iid == 0) continue;
                results.Add(ToSummary(issue, iid, prefix));
            }
        }
        return results;
    }

    private static IssueSummary ToSummary(JsonElement issue, int iid, string prefix)
    {
        var title = issue.TryGetProperty("title", out var t) ? t.GetString() ?? iid.ToString(CultureInfo.InvariantCulture) : iid.ToString(CultureInfo.InvariantCulture);
        var status = issue.TryGetProperty("state", out var s) ? s.GetString() : null;
        var url = issue.TryGetProperty("web_url", out var w) ? w.GetString() ?? "" : "";
        return new IssueSummary(iid.ToString(CultureInfo.InvariantCulture), title, status, WeightOrLabelPoints(issue, prefix), url);
    }

    private static double? WeightOrLabelPoints(JsonElement issue, string prefix)
    {
        if (issue.TryGetProperty("weight", out var weight) && weight.ValueKind == JsonValueKind.Number)
        {
            return weight.GetDouble();
        }

        if (issue.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labels.EnumerateArray())
            {
                var name = label.GetString();
                if (name is not null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(name[prefix.Length..].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }
        }
        return null;
    }

    private static string PointsPrefix(TrackerConnection connection) =>
        string.IsNullOrWhiteSpace(connection.StoryPointsFieldOverride)
            ? DefaultPointsLabelPrefix
            : connection.StoryPointsFieldOverride.Trim();

    /// <summary>The API base (scheme+host/api/v4) for the validated connection host.</summary>
    private string ApiBase(TrackerConnection connection)
    {
        var uri = _hostPolicy.Validate(connection.BaseUrl);
        return $"{uri.Scheme}://{uri.Host}/api/v4";
    }

    /// <summary>The API base plus the URL-encoded project path parsed from the repo URL.</summary>
    private (string ApiBase, string ProjectPath) ApiAndProject(TrackerConnection connection)
    {
        var uri = _hostPolicy.Validate(connection.BaseUrl);
        var path = uri.AbsolutePath.Trim('/');
        // Drop a trailing "/-/..." segment (e.g. .../-/issues) so only the project path remains.
        var dash = path.IndexOf("/-/", StringComparison.Ordinal);
        if (dash >= 0)
        {
            path = path[..dash];
        }
        if (path.Length == 0 || !path.Contains('/'))
        {
            throw new TrackerException(TrackerErrorKind.InvalidRequest,
                "The GitLab URL must point to a project, e.g. https://gitlab.com/group/project.");
        }
        return ($"{uri.Scheme}://{uri.Host}/api/v4", Uri.EscapeDataString(path));
    }

    private static int Iid(string key)
    {
        var trimmed = key.TrimStart('#').Trim();
        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iid))
        {
            throw new TrackerException(TrackerErrorKind.InvalidRequest, $"'{key}' is not a valid GitLab issue id.");
        }
        return iid;
    }

    private HttpClient CreateClient(TrackerConnection connection)
    {
        var client = _httpClientFactory.CreateClient("tracker");
        // GitLab PAT auth header.
        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", connection.Token);
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
            TrackerErrorKind.Unauthorized => "GitLab rejected the access token.",
            TrackerErrorKind.Forbidden => "The token lacks permission for this action.",
            TrackerErrorKind.NotFound => "The issue or project was not found.",
            TrackerErrorKind.RateLimited => "GitLab rate limit hit — try again shortly.",
            _ => $"GitLab request failed ({(int)response.StatusCode}).",
        };
        await Task.CompletedTask;
        throw new TrackerException(kind, message);
    }
}
