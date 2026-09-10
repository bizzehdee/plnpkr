using TeamTools.Core.Models;

namespace TeamTools.Core.Contracts;

/// <summary>
/// Serializable view of a retro board sent to clients on join/reconnect and after every change.
/// <para>
/// Room-level state (identity, participants, presence, organisers, closed) lives in the shared
/// <see cref="Room"/> fragment, defined once for both tools (#19); everything else here is
/// retrospective-specific. The projection is **per recipient** — the same mechanism poker uses to
/// hide votes before a reveal — which is what lets anonymity (#22) and hidden collection (#23) be
/// properties of the wire rather than of the UI.
/// </para>
/// </summary>
public record RetroBoardSnapshot(
    RoomSnapshot Room,
    RetroTemplate Template,
    bool Anonymous,
    /// <summary>
    /// False once the first card exists: <see cref="Anonymous"/> is then locked, because flipping it
    /// would retroactively expose or hide what people already wrote (#22).
    /// </summary>
    bool CanChangeAnonymity,
    IReadOnlyList<RetroColumnInfo> Columns);

/// <summary>A column and the cards in it, in display order.</summary>
public record RetroColumnInfo(
    Guid Id,
    string Title,
    int Order,
    IReadOnlyList<RetroCardInfo> Cards);

/// <summary>
/// A card as one recipient may see it.
/// <para>
/// <see cref="IsMine"/> tells the client it may edit this card without needing to know who wrote
/// it, which is what makes an anonymous board possible without a second code path: on such a board
/// <see cref="AuthorUserId"/> and <see cref="AuthorDisplayName"/> are null for **every** card,
/// including the recipient's own. Nulling them for everyone rather than "for everyone but you"
/// means no future field or code path can leak authorship by omission (#22).
/// </para>
/// </summary>
public record RetroCardInfo(
    Guid Id,
    string Text,
    string? AuthorUserId,
    string? AuthorDisplayName,
    bool IsMine,
    int Order,
    DateTimeOffset CreatedAt);

public enum RetroActionStatus
{
    Ok,
    /// <summary>No such room, or it isn't a retro board.</summary>
    BoardNotFound,
    NotParticipant,
    /// <summary>Caller is not an organiser of a board that has one. See #10.</summary>
    NotOrganiser,
    /// <summary>The board is closed (read-only) and can't be changed. See #26.</summary>
    BoardClosed,
    /// <summary>The referenced column isn't on this board.</summary>
    ColumnNotFound,
    /// <summary>The referenced card isn't on this board.</summary>
    CardNotFound,
    /// <summary>Only a card's author or an organiser may change it.</summary>
    NotCardAuthor,
    /// <summary>The card text is empty, or longer than the cap.</summary>
    InvalidCardText,
    /// <summary>Anonymity is locked because the board already has cards (#22).</summary>
    AnonymityLocked,
    /// <summary>The requested template is invalid (e.g. an empty custom layout).</summary>
    InvalidTemplate,
    /// <summary>Too many cards added too quickly (abuse throttle). See #3-abuse.</summary>
    RateLimited,
}

/// <summary>
/// Result of a retro mutation. <see cref="Board"/> is the post-change snapshot to broadcast when
/// <see cref="Status"/> is <see cref="RetroActionStatus.Ok"/>.
/// </summary>
public record RetroActionResult(RetroActionStatus Status, RetroBoardSnapshot? Board)
{
    public static RetroActionResult Ok(RetroBoardSnapshot board) => new(RetroActionStatus.Ok, board);
    public static RetroActionResult NotFound() => new(RetroActionStatus.BoardNotFound, null);
    public static RetroActionResult NotParticipant() => new(RetroActionStatus.NotParticipant, null);
    public static RetroActionResult NotOrganiser() => new(RetroActionStatus.NotOrganiser, null);
    public static RetroActionResult Closed() => new(RetroActionStatus.BoardClosed, null);
    public static RetroActionResult ColumnNotFound() => new(RetroActionStatus.ColumnNotFound, null);
    public static RetroActionResult CardNotFound() => new(RetroActionStatus.CardNotFound, null);
    public static RetroActionResult NotCardAuthor() => new(RetroActionStatus.NotCardAuthor, null);
    public static RetroActionResult InvalidCardText() => new(RetroActionStatus.InvalidCardText, null);
    public static RetroActionResult AnonymityLocked() => new(RetroActionStatus.AnonymityLocked, null);
    public static RetroActionResult InvalidTemplate() => new(RetroActionStatus.InvalidTemplate, null);
    public static RetroActionResult RateLimited() => new(RetroActionStatus.RateLimited, null);
}

/// <summary>Request to create a retro board. The creator becomes its first participant. See #21.</summary>
public record CreateRetroRequest(
    string Name,
    RetroTemplate Template,
    string? CustomColumns,
    string CreatorUserId,
    string CreatorDisplayName,
    bool Organise,
    string? Password = null,
    bool EnableReactions = true,
    /// <summary>Whether cards are anonymous (#22). Chosen up front, since it locks once cards exist.</summary>
    bool Anonymous = false);

public enum CreateRetroStatus
{
    Ok,
    InvalidName,
    InvalidTemplate,
    /// <summary>The caller is creating boards too quickly (abuse throttle). See #3-abuse.</summary>
    RateLimited,
}

public record CreateRetroResult(CreateRetroStatus Status, RetroBoardSnapshot? Board, string? Error)
{
    public static CreateRetroResult Ok(RetroBoardSnapshot board) => new(CreateRetroStatus.Ok, board, null);
    public static CreateRetroResult InvalidName(string error) => new(CreateRetroStatus.InvalidName, null, error);
    public static CreateRetroResult InvalidTemplate(string error) =>
        new(CreateRetroStatus.InvalidTemplate, null, error);
    public static CreateRetroResult RateLimited() =>
        new(CreateRetroStatus.RateLimited, null, "You're doing that too often — please wait a moment and try again.");
}

/// <summary>Result of joining a retro board, mirroring the poker join contract.</summary>
public record RetroJoinResult(
    JoinStatus Status,
    RetroBoardSnapshot? Board,
    ParticipantInfo? Participant,
    string? Error)
{
    public static RetroJoinResult Ok(RetroBoardSnapshot board, ParticipantInfo participant) =>
        new(JoinStatus.Ok, board, participant, null);
}
