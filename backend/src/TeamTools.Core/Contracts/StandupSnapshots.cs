using TeamTools.Core.Models;

namespace TeamTools.Core.Contracts;

/// <summary>
/// Serializable view of an Async Standup board (#36), sent on join/reconnect and after every change.
/// <para>
/// Projected **per recipient**, like the retro's and the coffee's, because this tool's one rule is a
/// property of the wire: you read other people's answers once you have posted your own. A
/// client-side hide would ship everyone's standup to every browser and hope nobody looked (#22).
/// </para>
/// </summary>
public record StandupBoardSnapshot(
    RoomSnapshot Room,
    IReadOnlyList<StandupQuestionInfo> Questions,
    /// <summary>Whether this viewer has posted, and therefore whether the answers below are real.</summary>
    bool IHavePosted,
    /// <summary>
    /// How many of the people in this room have posted. A count, never a list of who has not —
    /// presence only knows who opened the room, so naming the missing would name the wrong people
    /// (#36).
    /// </summary>
    int PostedCount,
    int ParticipantCount,
    /// <summary>
    /// Everyone's answers, grouped by person — but only once <see cref="IHavePosted"/> is true.
    /// Before that it contains this viewer's own answers and nothing else.
    /// </summary>
    IReadOnlyList<StandupPersonInfo> People,
    /// <summary>What is in someone's way, unresolved first.</summary>
    IReadOnlyList<StandupBlockerInfo> Blockers,
    /// <summary>The standup this one was started from, or null (#36).</summary>
    string? PreviousBoardShortCode);

public record StandupQuestionInfo(Guid Id, string Text, int Order);

/// <summary>One person's standup, as clients see it.</summary>
public record StandupPersonInfo(
    string UserId,
    string DisplayName,
    bool IsMe,
    /// <summary>Answers keyed by question id, in question order. Missing answers are simply absent.</summary>
    IReadOnlyList<StandupAnswerInfo> Answers,
    DateTimeOffset? PostedAt);

public record StandupAnswerInfo(
    Guid QuestionId,
    string Text,
    DateTimeOffset? UpdatedAt);

/// <summary>A blocker as clients see it. Same shape as an action item.</summary>
public record StandupBlockerInfo(
    Guid Id,
    string Text,
    string AuthorUserId,
    string AuthorDisplayName,
    string? OwnerUserId,
    string? OwnerName,
    bool IsResolved,
    bool CarriedOver,
    DateTimeOffset CreatedAt);

/// <summary>Request to open a standup. The creator becomes its first participant.</summary>
public record CreateStandupRequest(
    string Name,
    string CreatorUserId,
    string CreatorDisplayName,
    bool Organise,
    string? Password = null,
    bool EnableReactions = true,
    /// <summary>Questions to ask; null or empty takes the three almost every team already asks.</summary>
    IReadOnlyList<string>? Questions = null,
    /// <summary>
    /// A previous standup's short code to carry forward from (#36): its questions and its unresolved
    /// blockers. Its password, if it had one, must be supplied.
    /// </summary>
    string? PreviousBoardShortCode = null,
    string? PreviousBoardPassword = null);

public enum CreateStandupStatus
{
    Ok,
    InvalidName,
    InvalidQuestions,
    /// <summary>The standup to carry forward from does not exist.</summary>
    PreviousBoardNotFound,
    /// <summary>That standup is password-protected and the password was missing or wrong.</summary>
    PreviousBoardPasswordRequired,
    RateLimited,
}

public record CreateStandupResult(
    CreateStandupStatus Status, StandupBoardSnapshot? Board, string? Error)
{
    public static CreateStandupResult Ok(StandupBoardSnapshot board) =>
        new(CreateStandupStatus.Ok, board, null);

    public static CreateStandupResult InvalidName(string error) =>
        new(CreateStandupStatus.InvalidName, null, error);

    public static CreateStandupResult InvalidQuestions(string error) =>
        new(CreateStandupStatus.InvalidQuestions, null, error);

    public static CreateStandupResult PreviousNotFound() =>
        new(CreateStandupStatus.PreviousBoardNotFound, null,
            "That standup could not be found, so there is nothing to carry forward.");

    public static CreateStandupResult PreviousPasswordRequired() =>
        new(CreateStandupStatus.PreviousBoardPasswordRequired, null,
            "That standup has a password — enter it to carry its questions and blockers forward.");

    public static CreateStandupResult RateLimited() =>
        new(CreateStandupStatus.RateLimited, null,
            "You're doing that too often — please wait a moment and try again.");
}

public record StandupJoinResult(
    JoinStatus Status, StandupBoardSnapshot? Board, ParticipantInfo? Participant, string? Error)
{
    public static StandupJoinResult Ok(StandupBoardSnapshot board, ParticipantInfo participant) =>
        new(JoinStatus.Ok, board, participant, null);
}

/// <summary>Why a standup operation was refused, or Ok.</summary>
public enum StandupActionStatus
{
    Ok,
    BoardNotFound,
    NotParticipant,
    NotOrganiser,
    BoardClosed,
    QuestionNotFound,
    InvalidAnswer,
    BlockerNotFound,
    InvalidBlockerText,
    RateLimited,
}

public record StandupActionResult(StandupActionStatus Status, StandupBoardSnapshot? Board)
{
    public static StandupActionResult Ok(StandupBoardSnapshot board) =>
        new(StandupActionStatus.Ok, board);

    public static StandupActionResult NotFound() => new(StandupActionStatus.BoardNotFound, null);
    public static StandupActionResult NotParticipant() => new(StandupActionStatus.NotParticipant, null);
    public static StandupActionResult NotOrganiser() => new(StandupActionStatus.NotOrganiser, null);
    public static StandupActionResult Closed() => new(StandupActionStatus.BoardClosed, null);
    public static StandupActionResult QuestionNotFound() =>
        new(StandupActionStatus.QuestionNotFound, null);
    public static StandupActionResult InvalidAnswer() => new(StandupActionStatus.InvalidAnswer, null);
    public static StandupActionResult BlockerNotFound() =>
        new(StandupActionStatus.BlockerNotFound, null);
    public static StandupActionResult InvalidBlockerText() =>
        new(StandupActionStatus.InvalidBlockerText, null);
    public static StandupActionResult RateLimited() => new(StandupActionStatus.RateLimited, null);
}
