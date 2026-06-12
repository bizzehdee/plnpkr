using PlanningPoker.Core.Integrations;
using PlanningPoker.Core.Models;

namespace PlanningPoker.Core.Contracts;

/// <summary>
/// Serializable view of a session sent to clients on join/reconnect and after every change.
/// Vote values are only populated when <see cref="State"/> is <see cref="SessionState.Revealed"/>,
/// so the snapshot never leaks hidden votes. See #34.
/// </summary>
public record SessionSnapshot(
    Guid Id,
    string ShortCode,
    string Name,
    DeckType DeckType,
    IReadOnlyList<string> Cards,
    SessionState State,
    string? OrganiserUserId,
    bool AutoReveal,
    bool ReactionsEnabled,
    bool AllowRoleChange,
    bool IsClosed,
    string? CurrentStory,
    IReadOnlyList<ParticipantInfo> Participants,
    VoteStats? Stats,
    IntegrationInfo? Integration,
    // Round timer (#14). Duration is the configured length; Deadline is set while running (clients
    // tick locally against it); PausedRemainingSeconds is set while paused. All null ⇒ idle/no timer.
    int? TimerDurationSeconds,
    DateTimeOffset? TimerDeadline,
    int? TimerPausedRemainingSeconds);

/// <summary>
/// Broadcast-safe issue-tracker state for a session: the provider and the linked ticket. The
/// connection/token and per-user "connected" state are NOT here (see #4). Null when the
/// integration feature is off or no provider is linked.
/// </summary>
public record IntegrationInfo(
    IntegrationProvider Provider,
    LinkedIssueInfo? LinkedIssue,
    IReadOnlyList<QueuedTicketInfo> Queue);

public record LinkedIssueInfo(
    string Key,
    string Title,
    string? Description,
    string Url,
    double? StoryPoints,
    bool StoryPointsFieldAvailable);

/// <summary>A queue row for the UI; <see cref="IsSelected"/> marks the currently-linked ticket. See #38.</summary>
public record QueuedTicketInfo(
    string Key,
    string Title,
    string? Status,
    double? StoryPoints,
    string Url,
    bool IsSelected);

/// <summary>
/// Public view of a participant. <see cref="Vote"/> is null while votes are hidden and carries the
/// chosen card once revealed. <see cref="HasVoted"/> is always visible.
/// </summary>
public record ParticipantInfo(
    string UserId,
    string DisplayName,
    bool IsOrganiser,
    ParticipantRole Role,
    bool HasVoted,
    bool ChangedAfterReveal,
    string? Vote,
    bool IsConnected,
    bool IsOutlier);

/// <summary>Reveal statistics. Only meaningful when the session is revealed. See #28.</summary>
public record VoteStats(
    double? Average,
    bool Consensus,
    int VoteCount,
    IReadOnlyList<VoteCount> Distribution,
    double? Min,
    double? Max,
    double? StdDev,
    IReadOnlyList<string> OutlierValues);

public record VoteCount(string Value, int Count);
