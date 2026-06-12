namespace PlanningPoker.Core.Integrations;

/// <summary>
/// A resolved request for a *set* of issues, parsed from a board/query URL or a list of keys.
/// Adapters translate each shape to the right provider API. See #38.
/// </summary>
public abstract record IssueQuery;

/// <summary>Jira JQL search.</summary>
public sealed record JqlQuery(string Jql) : IssueQuery;

/// <summary>Jira agile board → its issues.</summary>
public sealed record JiraBoardQuery(string BoardId) : IssueQuery;

/// <summary>Jira saved filter → resolved to JQL, then searched.</summary>
public sealed record JiraFilterQuery(string FilterId) : IssueQuery;

/// <summary>Azure DevOps shared query (WIQL) within a project.</summary>
public sealed record AdoSharedQuery(string Project, string QueryId) : IssueQuery;

/// <summary>An explicit list of ticket keys/ids (the fallback input).</summary>
public sealed record KeyListQuery(IReadOnlyList<string> Keys) : IssueQuery;

/// <summary>Lightweight ticket row for the queue. Full description is fetched on select. See #38.</summary>
public record IssueSummary(string Key, string Title, string? Status, double? StoryPoints, string Url);

/// <summary>Parses a board/query URL into an <see cref="IssueQuery"/>. Pure; null if unrecognised.</summary>
public interface IBoardUrlParser
{
    IssueQuery? Parse(string url);
}
