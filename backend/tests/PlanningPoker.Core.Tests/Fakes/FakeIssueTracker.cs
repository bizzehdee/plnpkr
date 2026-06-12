using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Core.Tests.Fakes;

/// <summary>Configurable fake tracker so IntegrationService is tested without any HTTP.</summary>
public sealed class FakeIssueTracker : IIssueTracker, IIssueTrackerFactory
{
    public IntegrationProvider Provider { get; init; } = IntegrationProvider.Jira;

    public TrackerValidation Validation { get; set; } = new(true, "Test User", null);
    public TrackerException? ValidateThrows { get; set; }
    public TrackerException? GetIssueThrows { get; set; }
    public IssueDetails? Issue { get; set; }
    public double? LastSubmittedPoints { get; private set; }

    public TrackerConnection? LastConnection { get; private set; }

    public IIssueTracker For(IntegrationProvider provider) => this;

    public Task<TrackerValidation> ValidateAsync(TrackerConnection connection, CancellationToken cancellationToken = default)
    {
        LastConnection = connection;
        if (ValidateThrows is not null) throw ValidateThrows;
        return Task.FromResult(Validation);
    }

    public Task<IssueDetails> GetIssueAsync(TrackerConnection connection, string issueKey, CancellationToken cancellationToken = default)
    {
        LastConnection = connection;
        if (GetIssueThrows is not null) throw GetIssueThrows;
        return Task.FromResult(Issue ?? new IssueDetails(
            issueKey, $"Title for {issueKey}", "A description", $"https://example.atlassian.net/browse/{issueKey}",
            null, "customfield_10016", "Story point estimate"));
    }

    public TrackerException? SetStoryPointsThrows { get; set; }
    public string? LastSubmittedKey { get; private set; }

    public Task SetStoryPointsAsync(TrackerConnection connection, string issueKey, double points, CancellationToken cancellationToken = default)
    {
        LastConnection = connection;
        if (SetStoryPointsThrows is not null) throw SetStoryPointsThrows;
        LastSubmittedKey = issueKey;
        LastSubmittedPoints = points;
        return Task.CompletedTask;
    }

    public IReadOnlyList<IssueSummary> SearchResult { get; set; } = new List<IssueSummary>();
    public TrackerException? SearchThrows { get; set; }
    public IssueQuery? LastQuery { get; private set; }

    public Task<IReadOnlyList<IssueSummary>> SearchAsync(TrackerConnection connection, IssueQuery query, int maxResults, CancellationToken cancellationToken = default)
    {
        LastConnection = connection;
        LastQuery = query;
        if (SearchThrows is not null) throw SearchThrows;
        return Task.FromResult(SearchResult);
    }
}
