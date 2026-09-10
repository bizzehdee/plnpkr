namespace TeamTools.Core.Models;

/// <summary>
/// A ceremony room — the tool-agnostic half of what used to be <c>Session</c> (#19). It owns the
/// invite short code, the participants and everything true of *any* room: presence, the organiser
/// set, an optional join password, reactions, and the close/soft-delete lifecycle.
/// <para>
/// A room carries exactly one tool payload, fixed at creation by <see cref="Tool"/> and immutable
/// thereafter: <see cref="PokerRound"/> for estimation, <see cref="RetroBoard"/> for a retro,
/// <see cref="CoffeeBoard"/> for a Lean Coffee. The
/// split is what lets every room-level feature (rate limits #3, a11y #4, i18n #5, large-group #6,
/// multi-organiser #7, retention #15) serve both tools from one implementation.
/// </para>
/// </summary>
public class Room
{
    public Guid Id { get; set; }

    /// <summary>Short, URL-friendly slug used in the invite link (e.g. "blue-fox-42"). Unique. See #1.</summary>
    public string ShortCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Which tool this room hosts. Chosen at creation; never changes. See #19.</summary>
    public RoomTool Tool { get; set; } = RoomTool.Poker;

    /// <summary>The founding organiser's UserId, or null if the room has no organiser. See #10.</summary>
    public string? OrganiserUserId { get; set; }

    /// <summary>
    /// Optional join password, stored as a salted KDF hash (never plaintext). Null = no password. See #2.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>Whether emoji reactions are currently allowed (organiser-toggleable). See #17.</summary>
    public bool ReactionsEnabled { get; set; } = true;

    /// <summary>Whether participants may switch their own role mid-room (organiser-toggleable). See #21.</summary>
    public bool AllowRoleChange { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>When set, the room is closed (read-only). See #26.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>When set, the room is soft-deleted (hidden everywhere). See #26.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public List<Participant> Participants { get; set; } = new();

    /// <summary>The estimation payload — non-null exactly when <see cref="Tool"/> is Poker.</summary>
    public PokerRound? PokerRound { get; set; }

    /// <summary>The retrospective payload — non-null exactly when <see cref="Tool"/> is Retro.</summary>
    public RetroBoard? RetroBoard { get; set; }

    /// <summary>The Lean Coffee payload — non-null exactly when <see cref="Tool"/> is Coffee (#35).</summary>
    public CoffeeBoard? CoffeeBoard { get; set; }

    /// <summary>True once the room has been closed into its frozen read-only state (#26).</summary>
    public bool IsClosed => ClosedAt is not null;
}
