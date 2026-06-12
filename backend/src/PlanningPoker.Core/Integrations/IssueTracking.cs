namespace PlanningPoker.Core.Integrations;

/// <summary>Supported issue trackers. See #4.</summary>
public enum IntegrationProvider
{
    Jira,
    AzureDevOps,
}

/// <summary>How the token authenticates: HTTP Basic (PAT/API token) or Bearer (OAuth access token).</summary>
public enum AuthScheme
{
    Basic,
    Bearer,
}

/// <summary>
/// A live connection to an issue tracker for a session. Holds the secret token — kept in server
/// memory only, never persisted server-side or broadcast. See #4.
/// </summary>
public record TrackerConnection(
    IntegrationProvider Provider,
    string BaseUrl,
    string? Email,   // Jira PAT: the account email for basic auth (token is the API token)
    string Token,    // pasted API token / PAT, or an OAuth access token
    AuthScheme Scheme = AuthScheme.Basic,
    string? BrowseBaseUrl = null, // site URL for human "browse" links when BaseUrl is an API gateway (OAuth)
    string? StoryPointsFieldOverride = null); // optional manual field id or name when auto-detect fails (#41)

/// <summary>Result of validating a connection's credentials.</summary>
public record TrackerValidation(bool Ok, string? AccountName, string? Error);

/// <summary>The fields the poker app needs from a ticket. See #35.</summary>
public record IssueDetails(
    string Key,
    string Title,
    string? Description,
    string Url,
    double? CurrentStoryPoints,
    string? StoryPointsFieldId,
    string? StoryPointsFieldName)
{
    public bool StoryPointsFieldAvailable => StoryPointsFieldId is not null;
}

/// <summary>Why a tracker call failed, mapped to a friendly client message by the service.</summary>
public enum TrackerErrorKind
{
    Unauthorized,
    Forbidden,
    NotFound,
    RateLimited,
    InvalidRequest,
    Unavailable,
}

public sealed class TrackerException : Exception
{
    public TrackerException(TrackerErrorKind kind, string message) : base(message) => Kind = kind;

    public TrackerErrorKind Kind { get; }
}

/// <summary>
/// Provider-agnostic issue-tracker port. Implementations (Jira, Azure DevOps) live in the
/// PlanningPoker.Integrations adapter project; Core depends only on this interface so the
/// orchestration is unit-testable against a fake.
/// </summary>
public interface IIssueTracker
{
    IntegrationProvider Provider { get; }

    /// <summary>Verifies the credentials work (e.g. "who am I"). Throws <see cref="TrackerException"/> on failure.</summary>
    Task<TrackerValidation> ValidateAsync(TrackerConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Fetches a ticket's title/description and resolves its story-points field.</summary>
    Task<IssueDetails> GetIssueAsync(TrackerConnection connection, string issueKey, CancellationToken cancellationToken = default);

    /// <summary>Writes the story-points value back to the ticket (the adapter resolves the field).</summary>
    Task SetStoryPointsAsync(TrackerConnection connection, string issueKey, double points, CancellationToken cancellationToken = default);

    /// <summary>Returns a list of issue summaries for a query (board/filter/JQL/query/key-list). See #38.</summary>
    Task<IReadOnlyList<IssueSummary>> SearchAsync(TrackerConnection connection, IssueQuery query, int maxResults, CancellationToken cancellationToken = default);
}

/// <summary>Resolves the <see cref="IIssueTracker"/> for a provider.</summary>
public interface IIssueTrackerFactory
{
    IIssueTracker For(IntegrationProvider provider);
}
