namespace TeamTools.Core.Models;

/// <summary>
/// The retrospective payload of a retro <see cref="Room"/> (#21) — the column layout and the cards
/// on it. One-to-one with its room and keyed by <see cref="RoomId"/> (a shared primary key), so a
/// room and its board are always loaded and deleted together, exactly like a poker round.
/// </summary>
public class RetroBoard
{
    /// <summary>Primary key, shared with the owning <see cref="Room"/>.</summary>
    public Guid RoomId { get; set; }

    public Room? Room { get; set; }

    /// <summary>Which column layout the board started from. See <c>RetroTemplateCatalog</c>.</summary>
    public RetroTemplate Template { get; set; } = RetroTemplate.WentWellToImprove;

    /// <summary>
    /// When true, cards are anonymous: the snapshot carries **no** authorship at all, for anyone
    /// (#22). Only settable while the board is empty — flipping it later would retroactively expose
    /// cards written under a promise of anonymity, or retroactively hide attributed ones.
    /// <para>
    /// This is anonymity <em>from participants</em>, not from a database administrator:
    /// <see cref="RetroCard.AuthorUserId"/> is still stored, because an author has to be able to
    /// edit their own card and an organiser needs a target to moderate. The UI must not imply more.
    /// </para>
    /// </summary>
    public bool Anonymous { get; set; }

    /// <summary>Where the facilitator has moved the retro to. See #23.</summary>
    public RetroPhase Phase { get; set; } = RetroPhase.Collect;

    /// <summary>
    /// Configured phase-countdown length in seconds, or null for no countdown. See #23.
    /// </summary>
    public int? PhaseDurationSeconds { get; set; }

    /// <summary>
    /// UTC instant the running phase countdown expires; null when no countdown is running. Reuses
    /// the deadline-broadcast pattern the poker round timer established (#14), so clients tick
    /// locally against one server-authoritative instant.
    /// </summary>
    public DateTimeOffset? PhaseDeadline { get; set; }

    /// <summary>
    /// The board's columns, in display order. Materialised from the template at creation (rather than
    /// resolved on every read) because a card belongs to a column and a custom layout has to persist.
    /// </summary>
    public List<RetroColumn> Columns { get; set; } = new();

    /// <summary>Every card on the board, across all columns.</summary>
    public List<RetroCard> Cards { get; set; } = new();

    /// <summary>The themes cards have been grouped into. See #24.</summary>
    public List<RetroGroup> Groups { get; set; } = new();

    /// <summary>Every dot spent on this board. One row per dot. See #25.</summary>
    public List<RetroVote> Votes { get; set; } = new();

    /// <summary>How many dots each participant gets to spend. See #25.</summary>
    public int VoteBudget { get; set; } = DefaultVoteBudget;

    /// <summary>
    /// Whether a voter may stack more than one dot on the same item. Off by default: spreading dots
    /// surfaces more of what the team cares about, which is the point of dot voting.
    /// </summary>
    public bool AllowMultiplePerItem { get; set; }

    /// <summary>Three dots is the usual facilitation default — enough to rank, few enough to force a choice.</summary>
    public const int DefaultVoteBudget = 3;

    /// <summary>
    /// When true, any participant may group cards — not just an organiser (#24). Off by default:
    /// grouping is a facilitation act, and two people dragging the same card in opposite directions
    /// is a worse experience than waiting for the facilitator.
    /// </summary>
    public bool AllowParticipantGrouping { get; set; }
}

/// <summary>
/// A theme: several cards that say the same thing, gathered so the team discusses them once and
/// votes on them once (#24). Twelve cards about slow CI should not out-vote one card about a real
/// problem simply by being twelve.
/// </summary>
public class RetroGroup
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public RetroBoard? Board { get; set; }

    /// <summary>
    /// The theme's name. Seeded from the first card's text when the group is formed, because an
    /// unnamed group is harder to discuss than a badly named one — the facilitator renames it.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Display position among the groups, ascending.</summary>
    public int Order { get; set; }

    public List<RetroCard> Cards { get; set; } = new();
}

/// <summary>A column on a retro board — one of the prompts the team writes cards against.</summary>
public class RetroColumn
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public RetroBoard? Board { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Display position, ascending. Assigned from the template at creation.</summary>
    public int Order { get; set; }

    public List<RetroCard> Cards { get; set; } = new();
}

/// <summary>
/// One card written by a participant (#21). <see cref="AuthorUserId"/> is stored so an author can
/// edit their own card and an organiser can moderate — note that anonymity (#22) hides it on the
/// wire rather than by leaving it unstored.
/// </summary>
public class RetroCard
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public RetroBoard? Board { get; set; }

    public Guid ColumnId { get; set; }

    public RetroColumn? Column { get; set; }

    /// <summary>The theme this card belongs to, or null when it stands alone. See #24.</summary>
    public Guid? GroupId { get; set; }

    public RetroGroup? Group { get; set; }

    /// <summary>The stable per-browser id of whoever wrote it. See #34.</summary>
    public string AuthorUserId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Position within its column, ascending.</summary>
    public int Order { get; set; }
}

/// <summary>What a dot can be spent on (#25). A theme and a loose card are both votable.</summary>
public enum RetroVoteTarget
{
    Card,
    Group,
}

/// <summary>
/// One dot, spent by one participant on one item (#25). A row per dot rather than a count, so
/// withdrawing a single dot is a row delete and the budget is simply a row count — no arithmetic to
/// get wrong, and no way for a client-supplied total to be believed.
/// </summary>
public class RetroVote
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public RetroBoard? Board { get; set; }

    /// <summary>Who spent it. Never sent to other participants — only tallies are. See #25.</summary>
    public string VoterUserId { get; set; } = string.Empty;

    public RetroVoteTarget TargetKind { get; set; }

    /// <summary>The card or group id, per <see cref="TargetKind"/>.</summary>
    public Guid TargetId { get; set; }
}
