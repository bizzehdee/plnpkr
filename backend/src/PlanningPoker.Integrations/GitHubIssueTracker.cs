using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Integrations;

/// <summary>
/// GitHub Issues REST adapter (#13). The connection BaseUrl is the repository web URL
/// (https://github.com/{owner}/{repo}); the host policy validates it, and API calls go to the
/// fixed, trusted api.github.com. Auth is a PAT (or OAuth token) via Bearer. GitHub has no native
/// story-points field, so points are stored in a label — default prefix "points:" (e.g. "points: 5"),
/// overridable via the connection's story-points field (#41). Issue "key" is the issue number.
/// </summary>
public sealed class GitHubIssueTracker : IIssueTracker
{
    private const string ApiBase = "https://api.github.com/";
    private const string DefaultPointsLabelPrefix = "points:";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITrackerHostPolicy _hostPolicy;
    private readonly IHtmlDescriptionSanitizer _sanitizer;

    public GitHubIssueTracker(IHttpClientFactory httpClientFactory, ITrackerHostPolicy hostPolicy, IHtmlDescriptionSanitizer sanitizer)
    {
        _httpClientFactory = httpClientFactory;
        _hostPolicy = hostPolicy;
        _sanitizer = sanitizer;
    }

    public IntegrationProvider Provider => IntegrationProvider.GitHub;

    public async Task<TrackerValidation> ValidateAsync(TrackerConnection connection, CancellationToken cancellationToken = default)
    {
        // Validate the repo URL host (SSRF guard) even though the call targets api.github.com.
        _ = ParseRepo(connection);
        using var client = CreateClient(connection);

        using var response = await client.GetAsync(new Uri(ApiBase + "user"), cancellationToken);
        await ThrowIfFailed(response);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var login = user.TryGetProperty("login", out var l) ? l.GetString() : null;
        return new TrackerValidation(true, login, null);
    }

    public async Task<IssueDetails> GetIssueAsync(TrackerConnection connection, string issueKey, CancellationToken cancellationToken = default)
    {
        var (owner, repo) = ParseRepo(connection);
        using var client = CreateClient(connection);
        var number = IssueNumber(issueKey);

        using var response = await client.GetAsync(new Uri($"{ApiBase}repos/{owner}/{repo}/issues/{number}"), cancellationToken);
        await ThrowIfFailed(response);
        var issue = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var title = issue.TryGetProperty("title", out var t) ? t.GetString() ?? issueKey : issueKey;
        // GitHub bodies are Markdown, not HTML; sanitise (a no-op for plain text) and present as-is.
        var description = issue.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
            ? _sanitizer.Sanitize(b.GetString())
            : null;
        var url = issue.TryGetProperty("html_url", out var h) ? h.GetString() ?? RepoUrl(owner, repo, number) : RepoUrl(owner, repo, number);
        var prefix = PointsPrefix(connection);
        var points = PointsFromLabels(issue, prefix);

        // Points live in a label, so the field is always "available" for an estimable issue.
        return new IssueDetails(number.ToString(CultureInfo.InvariantCulture), title, description, url, points, prefix, "Points label");
    }

    public async Task SetStoryPointsAsync(TrackerConnection connection, string issueKey, double points, CancellationToken cancellationToken = default)
    {
        var (owner, repo) = ParseRepo(connection);
        using var client = CreateClient(connection);
        var number = IssueNumber(issueKey);
        var prefix = PointsPrefix(connection);

        // Read the current labels, drop any existing points label, add the new one, and replace the set.
        using var get = await client.GetAsync(new Uri($"{ApiBase}repos/{owner}/{repo}/issues/{number}"), cancellationToken);
        await ThrowIfFailed(get);
        var issue = await get.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var labels = LabelNames(issue)
            .Where(n => !n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        labels.Add($"{prefix} {FormatPoints(points)}");

        var payload = JsonSerializer.Serialize(new { labels });
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri($"{ApiBase}repos/{owner}/{repo}/issues/{number}"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request, cancellationToken);
        await ThrowIfFailed(response);
    }

    public async Task<IReadOnlyList<IssueSummary>> SearchAsync(TrackerConnection connection, IssueQuery query, int maxResults, CancellationToken cancellationToken = default)
    {
        var (owner, repo) = ParseRepo(connection);
        using var client = CreateClient(connection);
        var prefix = PointsPrefix(connection);

        return query switch
        {
            KeyListQuery keys => await FetchEachAsync(client, owner, repo, keys.Keys.Take(maxResults), prefix, cancellationToken),
            OpenIssuesQuery => await ListOpenAsync(client, owner, repo, prefix, maxResults, cancellationToken),
            _ => throw new TrackerException(TrackerErrorKind.InvalidRequest, "Unsupported query type for GitHub."),
        };
    }

    private async Task<IReadOnlyList<IssueSummary>> FetchEachAsync(
        HttpClient client, string owner, string repo, IEnumerable<string> keys, string prefix, CancellationToken ct)
    {
        var results = new List<IssueSummary>();
        foreach (var key in keys)
        {
            var number = IssueNumber(key);
            using var response = await client.GetAsync(new Uri($"{ApiBase}repos/{owner}/{repo}/issues/{number}"), ct);
            if (!response.IsSuccessStatusCode)
            {
                continue; // skip an unreadable/missing issue rather than failing the whole queue
            }
            var issue = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            results.Add(ToSummary(issue, owner, repo, number, prefix));
        }
        return results;
    }

    private async Task<IReadOnlyList<IssueSummary>> ListOpenAsync(
        HttpClient client, string owner, string repo, string prefix, int maxResults, CancellationToken ct)
    {
        var perPage = Math.Clamp(maxResults, 1, 100);
        using var response = await client.GetAsync(
            new Uri($"{ApiBase}repos/{owner}/{repo}/issues?state=open&per_page={perPage}"), ct);
        await ThrowIfFailed(response);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        var results = new List<IssueSummary>();
        if (doc.ValueKind == JsonValueKind.Array)
        {
            foreach (var issue in doc.EnumerateArray())
            {
                // The issues endpoint also returns PRs; skip those.
                if (issue.TryGetProperty("pull_request", out _))
                {
                    continue;
                }
                var number = issue.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
                if (number == 0) continue;
                results.Add(ToSummary(issue, owner, repo, number, prefix));
            }
        }
        return results;
    }

    private static IssueSummary ToSummary(JsonElement issue, string owner, string repo, int number, string prefix)
    {
        var title = issue.TryGetProperty("title", out var t) ? t.GetString() ?? number.ToString(CultureInfo.InvariantCulture) : number.ToString(CultureInfo.InvariantCulture);
        var status = issue.TryGetProperty("state", out var s) ? s.GetString() : null;
        var url = issue.TryGetProperty("html_url", out var h) ? h.GetString() ?? RepoUrl(owner, repo, number) : RepoUrl(owner, repo, number);
        return new IssueSummary(number.ToString(CultureInfo.InvariantCulture), title, status, PointsFromLabels(issue, prefix), url);
    }

    private static double? PointsFromLabels(JsonElement issue, string prefix)
    {
        foreach (var name in LabelNames(issue))
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(name[prefix.Length..].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }
        return null;
    }

    private static IEnumerable<string> LabelNames(JsonElement issue)
    {
        if (issue.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labels.EnumerateArray())
            {
                var name = label.ValueKind == JsonValueKind.String
                    ? label.GetString()
                    : label.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.IsNullOrEmpty(name))
                {
                    yield return name;
                }
            }
        }
    }

    private static string FormatPoints(double points) =>
        points == Math.Floor(points)
            ? ((long)points).ToString(CultureInfo.InvariantCulture)
            : points.ToString(CultureInfo.InvariantCulture);

    private static string PointsPrefix(TrackerConnection connection) =>
        string.IsNullOrWhiteSpace(connection.StoryPointsFieldOverride)
            ? DefaultPointsLabelPrefix
            : connection.StoryPointsFieldOverride.Trim();

    /// <summary>Parses owner/repo from the validated repo URL (https://github.com/{owner}/{repo}).</summary>
    private (string Owner, string Repo) ParseRepo(TrackerConnection connection)
    {
        var uri = _hostPolicy.Validate(connection.BaseUrl);
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            throw new TrackerException(TrackerErrorKind.InvalidRequest,
                "The GitHub URL must point to a repository, e.g. https://github.com/owner/repo.");
        }
        return (segments[0], segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segments[1][..^4] : segments[1]);
    }

    private static int IssueNumber(string key)
    {
        var trimmed = key.TrimStart('#').Trim();
        // Accept "owner/repo#123" or "#123" or "123".
        var hash = trimmed.LastIndexOf('#');
        if (hash >= 0)
        {
            trimmed = trimmed[(hash + 1)..];
        }
        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            throw new TrackerException(TrackerErrorKind.InvalidRequest, $"'{key}' is not a valid GitHub issue number.");
        }
        return number;
    }

    private static string RepoUrl(string owner, string repo, int number) => $"https://github.com/{owner}/{repo}/issues/{number}";

    private HttpClient CreateClient(TrackerConnection connection)
    {
        var client = _httpClientFactory.CreateClient("tracker");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // GitHub requires a User-Agent on every request.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("plnpkr/1.0");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
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
            TrackerErrorKind.Unauthorized => "GitHub rejected the access token.",
            TrackerErrorKind.Forbidden => "The token lacks permission (or the rate limit was hit).",
            TrackerErrorKind.NotFound => "The issue or repository was not found.",
            TrackerErrorKind.RateLimited => "GitHub rate limit hit — try again shortly.",
            _ => $"GitHub request failed ({(int)response.StatusCode}).",
        };
        await Task.CompletedTask;
        throw new TrackerException(kind, message);
    }
}
