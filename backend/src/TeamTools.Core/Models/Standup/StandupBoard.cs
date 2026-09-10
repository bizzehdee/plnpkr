namespace TeamTools.Core.Models;

/// <summary>
/// The Async Standup payload of a standup <see cref="Room"/> (#36) — the questions, everyone's
/// answers, and the blockers someone needs to unblock.
/// <para>
/// One-to-one with its room and keyed by <see cref="RoomId"/>, like every other tool payload.
/// </para>
/// <para>
/// <b>This tool has no phase rail, deliberately.</b> A standup opens, people post, it closes —
/// there is nothing for a facilitator to move the room through. That is the point: the rail
/// generalised in #35 is a retro/coffee concern, not a platform one, and reaching for it here just
/// because it exists would be the wrong kind of reuse.
/// </para>
/// </summary>
public class StandupBoard
{
    /// <summary>Primary key, shared with the owning <see cref="Room"/>.</summary>
    public Guid RoomId { get; set; }

    public Room? Room { get; set; }

    /// <summary>
    /// The short code of the standup this one was started from (#36) — provenance for the humans.
    /// The questions and unresolved blockers were copied, so this board is self-contained and
    /// survives the previous one's retention delete, exactly as retro carry-over works (#27).
    /// </summary>
    public string? PreviousBoardShortCode { get; set; }

    public List<StandupQuestion> Questions { get; set; } = new();

    public List<StandupEntry> Entries { get; set; } = new();

    public List<StandupBlocker> Blockers { get; set; } = new();

    /// <summary>
    /// Whether <paramref name="userId"/> has posted anything yet — the gate on reading everyone
    /// else's answers. One answer to any question counts: the rule is "contribute before you read",
    /// not "fill in the form completely".
    /// </summary>
    public bool HasPosted(string userId) =>
        Entries.Any(e => e.AuthorUserId == userId && e.Text.Length > 0);
}

/// <summary>One of the questions this standup asks (#36).</summary>
public class StandupQuestion
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public StandupBoard? Board { get; set; }

    public string Text { get; set; } = string.Empty;

    public int Order { get; set; }
}

/// <summary>
/// One person's answer to one question (#36). A row per (person, question), created on first save
/// and updated after that — so "I posted" is a fact about rows rather than a flag to keep in sync.
/// </summary>
public class StandupEntry
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public StandupBoard? Board { get; set; }

    public Guid QuestionId { get; set; }

    public string AuthorUserId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it was last edited, or null if never — shown so a late edit is not invisible.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Something in someone's way (#36) — the only structured field on this board, because "who is
/// unblocking this" is the only decision a standup actually produces.
/// <para>
/// Same shape and the same rules as a retro action item and a coffee decision
/// (<see cref="ActionItemRules"/>): an optional owner who need not be in the room, and a resolved
/// state that gets ticked later.
/// </para>
/// </summary>
public class StandupBlocker
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public StandupBoard? Board { get; set; }

    /// <summary>Who is blocked.</summary>
    public string AuthorUserId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>Who is unblocking it, when someone has taken it on.</summary>
    public string? OwnerUserId { get; set; }

    public string? OwnerName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it was cleared, or null while it is still in the way.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>
    /// The board this blocker was carried forward from (#36). Provenance only — it is a copy, like
    /// a carried retro action (#27).
    /// </summary>
    public Guid? CarriedFromBoardId { get; set; }

    public bool IsResolved => ResolvedAt is not null;
}
