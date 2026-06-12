using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Integrations;

/// <summary>Resolves the registered <see cref="IIssueTracker"/> for a provider. See #4.</summary>
public sealed class IssueTrackerFactory : IIssueTrackerFactory
{
    private readonly IReadOnlyDictionary<IntegrationProvider, IIssueTracker> _trackers;

    public IssueTrackerFactory(IEnumerable<IIssueTracker> trackers) =>
        _trackers = trackers.ToDictionary(t => t.Provider);

    public IIssueTracker For(IntegrationProvider provider) =>
        _trackers.TryGetValue(provider, out var tracker)
            ? tracker
            : throw new TrackerException(TrackerErrorKind.InvalidRequest, $"Provider '{provider}' is not configured.");
}
