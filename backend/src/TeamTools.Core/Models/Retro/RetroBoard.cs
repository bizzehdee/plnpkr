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
    /// The board's columns, in display order. Materialised from the template at creation (rather than
    /// resolved on every read) because a card belongs to a column and a custom layout has to persist.
    /// </summary>
    public List<RetroColumn> Columns { get; set; } = new();

    /// <summary>Every card on the board, across all columns.</summary>
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

    /// <summary>The stable per-browser id of whoever wrote it. See #34.</summary>
    public string AuthorUserId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Position within its column, ascending.</summary>
    public int Order { get; set; }
}
