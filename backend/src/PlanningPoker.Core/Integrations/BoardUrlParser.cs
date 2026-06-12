using System.Text.RegularExpressions;

namespace PlanningPoker.Core.Integrations;

/// <summary>
/// Recognises common Jira and Azure DevOps board / filter / query / search URLs and turns them into
/// an <see cref="IssueQuery"/>. Pure and deterministic; returns null for shapes it doesn't know (the
/// caller then falls back to the paste-IDs path). See #38.
/// </summary>
public sealed class BoardUrlParser : IBoardUrlParser
{
    private static readonly Regex JiraBoard = new(@"/boards/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AdoQuery = new(@"/(?<project>[^/]+)/_queries/query/(?<id>[0-9a-fA-F-]{36})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IssueQuery? Parse(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var query = ParseQueryString(uri.Query);

        // --- Azure DevOps: .../{project}/_queries/query/{guid}[/...] ---
        var ado = AdoQuery.Match(uri.AbsolutePath);
        if (uri.Host.Contains("azure.com") || uri.Host.Contains("visualstudio.com"))
        {
            if (ado.Success)
            {
                return new AdoSharedQuery(Uri.UnescapeDataString(ado.Groups["project"].Value), ado.Groups["id"].Value);
            }
        }

        // --- Jira ---
        // Saved filter: ?filter=10001  (or /issues/?filter=...)
        if (query.TryGetValue("filter", out var filterId) && IsId(filterId))
        {
            return new JiraFilterQuery(filterId);
        }

        // Explicit JQL in the URL: /issues/?jql=...
        if (query.TryGetValue("jql", out var jql) && !string.IsNullOrWhiteSpace(jql))
        {
            return new JqlQuery(jql);
        }

        // Board: /jira/software/(c/)?projects/PROJ/boards/123  or  rapidView=123
        if (query.TryGetValue("rapidView", out var rapidView) && IsId(rapidView))
        {
            return new JiraBoardQuery(rapidView);
        }
        var board = JiraBoard.Match(uri.AbsolutePath);
        if (board.Success)
        {
            return new JiraBoardQuery(board.Groups["id"].Value);
        }

        return null;
    }

    private static bool IsId(string? s) => !string.IsNullOrEmpty(s) && s.All(char.IsDigit);

    private static Dictionary<string, string> ParseQueryString(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }
            var key = Uri.UnescapeDataString(pair[..idx]);
            var value = Uri.UnescapeDataString(pair[(idx + 1)..]);
            result[key] = value;
        }
        return result;
    }
}
