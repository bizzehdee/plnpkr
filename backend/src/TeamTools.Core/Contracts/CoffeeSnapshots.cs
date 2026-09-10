using TeamTools.Core.Models;

namespace TeamTools.Core.Contracts;

/// <summary>
/// Serializable view of a Lean Coffee board (#35), sent on join/reconnect and after every change.
/// <para>
/// Room-level state lives in the shared <see cref="RoomSnapshot"/> fragment, defined once for every
/// tool (#19). The projection is **per recipient**, like the retro's, because what a viewer may see
/// differs: other people's topics are hidden during Propose, dot totals are hidden while voting is
/// open, and individual extension answers are hidden while that vote is running.
/// </para>
/// </summary>
public record CoffeeBoardSnapshot(
    RoomSnapshot Room,
    CoffeePhase Phase,
    /// <summary>Null at either end of the rail — nothing to advance to or step back from.</summary>
    CoffeePhase? NextPhase,
    CoffeePhase? PreviousPhase,
    /// <summary>The per-topic timebox, and the running deadline clients tick against (#34).</summary>
    int? PhaseDurationSeconds,
    DateTimeOffset? PhaseDeadline,
    /// <summary>Dot-voting settings and this viewer's remaining allowance.</summary>
    int VoteBudget,
    bool AllowMultiplePerItem,
    int MyDotsRemaining,
    /// <summary>Whether dot totals are being sent at all. False while voting is open.</summary>
    bool VoteTotalsVisible,
    /// <summary>
    /// Topics as this viewer may see them: during Propose, only their own, plus a count of how many
    /// others are being written.
    /// </summary>
    IReadOnlyList<CoffeeTopicInfo> Topics,
    /// <summary>How many topics other people have proposed but this viewer may not read yet.</summary>
    int HiddenTopicCount,
    /// <summary>The agenda: topics ranked by dots, highest first. Empty until totals are visible.</summary>
    IReadOnlyList<CoffeeTopicInfo> Agenda,
    /// <summary>The topic under discussion, or null.</summary>
    Guid? CurrentTopicId,
    /// <summary>The extension vote, when one is running.</summary>
    CoffeeExtendVoteInfo? ExtendVote,
    /// <summary>What the room decided, outstanding first.</summary>
    IReadOnlyList<CoffeeDecisionInfo> Decisions);

/// <summary>A topic as clients see it (#35). Never anonymous — a topic is volunteered.</summary>
public record CoffeeTopicInfo(
    Guid Id,
    string Text,
    string AuthorUserId,
    string AuthorDisplayName,
    /// <summary>Whether this viewer proposed it, so the UI can offer edit/withdraw.</summary>
    bool IsMine,
    int Order,
    /// <summary>Total dots. Null while voting is open, for the same reason the retro's are.</summary>
    int? TotalDots,
    /// <summary>This viewer's own dots — always theirs to see.</summary>
    int MyDots,
    bool IsDiscussed,
    /// <summary>Seconds actually spent on it, and how many times the room chose to keep going.</summary>
    int DiscussedSeconds,
    int Extensions);

/// <summary>
/// A running extension vote (#35). While it is open only the *count* of answers is sent, never who
/// answered or which way — the room should not follow whoever clicked first. On resolve the split is
/// revealed.
/// </summary>
public record CoffeeExtendVoteInfo(
    Guid TopicId,
    int Answered,
    /// <summary>This viewer's own answer, or null if they have not voted.</summary>
    ExtendChoice? MyChoice,
    /// <summary>The split, once resolved. Null while the vote is open.</summary>
    int? KeepGoing,
    int? MoveOn);

/// <summary>A decision as clients see it (#35). Same shape as a retro action item.</summary>
public record CoffeeDecisionInfo(
    Guid Id,
    string Title,
    Guid? TopicId,
    string? OwnerUserId,
    string? OwnerName,
    DateTimeOffset? DueDate,
    bool IsDone,
    DateTimeOffset CreatedAt);

/// <summary>Request to create a Lean Coffee. The creator becomes its first participant.</summary>
public record CreateCoffeeRequest(
    string Name,
    string CreatorUserId,
    string CreatorDisplayName,
    bool Organise,
    string? Password = null,
    bool EnableReactions = true,
    /// <summary>Per-topic timebox in seconds; null takes the five-minute default.</summary>
    int? TimeboxSeconds = null);

public enum CreateCoffeeStatus
{
    Ok,
    InvalidName,
    RateLimited,
}

public record CreateCoffeeResult(CreateCoffeeStatus Status, CoffeeBoardSnapshot? Board, string? Error)
{
    public static CreateCoffeeResult Ok(CoffeeBoardSnapshot board) =>
        new(CreateCoffeeStatus.Ok, board, null);

    public static CreateCoffeeResult InvalidName(string error) =>
        new(CreateCoffeeStatus.InvalidName, null, error);

    public static CreateCoffeeResult RateLimited() =>
        new(CreateCoffeeStatus.RateLimited, null,
            "You're doing that too often — please wait a moment and try again.");
}

public record CoffeeJoinResult(
    JoinStatus Status, CoffeeBoardSnapshot? Board, ParticipantInfo? Participant, string? Error)
{
    public static CoffeeJoinResult Ok(CoffeeBoardSnapshot board, ParticipantInfo participant) =>
        new(JoinStatus.Ok, board, participant, null);
}

/// <summary>Why a Lean Coffee operation was refused, or Ok. Mirrors the retro's shape (#21).</summary>
public enum CoffeeActionStatus
{
    Ok,
    BoardNotFound,
    NotParticipant,
    NotOrganiser,
    BoardClosed,
    TopicNotFound,
    NotTopicAuthor,
    InvalidTopicText,
    WrongPhase,
    IllegalPhaseTransition,
    OutOfDots,
    AlreadyVotedForItem,
    NoVoteToWithdraw,
    InvalidVoteBudget,
    NoCurrentTopic,
    NoExtendVoteRunning,
    DecisionNotFound,
    InvalidDecisionTitle,
    RateLimited,
}

public record CoffeeActionResult(CoffeeActionStatus Status, CoffeeBoardSnapshot? Board)
{
    public static CoffeeActionResult Ok(CoffeeBoardSnapshot board) =>
        new(CoffeeActionStatus.Ok, board);

    public static CoffeeActionResult NotFound() => new(CoffeeActionStatus.BoardNotFound, null);
    public static CoffeeActionResult NotParticipant() => new(CoffeeActionStatus.NotParticipant, null);
    public static CoffeeActionResult NotOrganiser() => new(CoffeeActionStatus.NotOrganiser, null);
    public static CoffeeActionResult Closed() => new(CoffeeActionStatus.BoardClosed, null);
    public static CoffeeActionResult TopicNotFound() => new(CoffeeActionStatus.TopicNotFound, null);
    public static CoffeeActionResult NotTopicAuthor() => new(CoffeeActionStatus.NotTopicAuthor, null);
    public static CoffeeActionResult InvalidTopicText() => new(CoffeeActionStatus.InvalidTopicText, null);
    public static CoffeeActionResult WrongPhase() => new(CoffeeActionStatus.WrongPhase, null);
    public static CoffeeActionResult IllegalPhaseTransition() =>
        new(CoffeeActionStatus.IllegalPhaseTransition, null);
    public static CoffeeActionResult OutOfDots() => new(CoffeeActionStatus.OutOfDots, null);
    public static CoffeeActionResult AlreadyVotedForItem() =>
        new(CoffeeActionStatus.AlreadyVotedForItem, null);
    public static CoffeeActionResult NoVoteToWithdraw() =>
        new(CoffeeActionStatus.NoVoteToWithdraw, null);
    public static CoffeeActionResult InvalidVoteBudget() =>
        new(CoffeeActionStatus.InvalidVoteBudget, null);
    public static CoffeeActionResult NoCurrentTopic() => new(CoffeeActionStatus.NoCurrentTopic, null);
    public static CoffeeActionResult NoExtendVoteRunning() =>
        new(CoffeeActionStatus.NoExtendVoteRunning, null);
    public static CoffeeActionResult DecisionNotFound() =>
        new(CoffeeActionStatus.DecisionNotFound, null);
    public static CoffeeActionResult InvalidDecisionTitle() =>
        new(CoffeeActionStatus.InvalidDecisionTitle, null);
    public static CoffeeActionResult RateLimited() => new(CoffeeActionStatus.RateLimited, null);
}
