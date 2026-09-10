using System.Text.Json.Serialization;
using TeamTools.Core.Integrations;
using TeamTools.Core.Models;

namespace TeamTools.Core.Contracts;

/// <summary>
/// Serializable view of a poker session sent to clients on join/reconnect and after every change.
/// <para>
/// Room-level state (identity, participants, presence, organisers, closed) lives in the shared
/// <see cref="Room"/> fragment so it is defined once for both tools (#19); everything else here is
/// estimation-specific. Vote values are only populated when <see cref="State"/> is
/// <see cref="SessionState.Revealed"/> (or Discussion, a post-reveal phase), so the snapshot never
/// leaks hidden votes. See #34.
/// </para>
/// </summary>
public record SessionSnapshot(
    RoomSnapshot Room,
    DeckType DeckType,
    IReadOnlyList<string> Cards,
    SessionState State,
    bool AutoReveal,
    string? CurrentStory,
    string? CurrentStoryNote,
    VoteStats? Stats,
    IntegrationInfo? Integration,
    // Round timer (#14). Duration is the configured length; Deadline is set while running (clients
    // tick locally against it); PausedRemainingSeconds is set while paused. All null ⇒ idle/no timer.
    int? TimerDurationSeconds,
    DateTimeOffset? TimerDeadline,
    int? TimerPausedRemainingSeconds)
{
    // --- Room-level convenience accessors ----------------------------------
    // The room fragment is the single definition of these on the wire; these pass-throughs are a
    // server-side reading convenience only. [JsonIgnore] matters: without it every participant would
    // be serialized twice, which is exactly the payload bloat large-group mode (#6) guards against.
    [JsonIgnore] public Guid Id => Room.Id;
    [JsonIgnore] public string ShortCode => Room.ShortCode;
    [JsonIgnore] public string Name => Room.Name;
    [JsonIgnore] public string? OrganiserUserId => Room.OrganiserUserId;
    [JsonIgnore] public bool ReactionsEnabled => Room.ReactionsEnabled;
    [JsonIgnore] public bool AllowRoleChange => Room.AllowRoleChange;
    [JsonIgnore] public bool IsClosed => Room.IsClosed;
    [JsonIgnore] public IReadOnlyList<ParticipantInfo> Participants => Room.Participants;
}

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
