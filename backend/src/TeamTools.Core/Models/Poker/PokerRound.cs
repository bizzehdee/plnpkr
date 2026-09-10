using TeamTools.Core.Integrations;

namespace TeamTools.Core.Models;

/// <summary>
/// The ticket a poker round is linked to (broadcast-safe content; may persist). The connection/token
/// that produced it is NOT here — that lives in the in-memory connection store. See #4.
/// </summary>
public class LinkedIssue
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Url { get; set; } = string.Empty;
    public double? StoryPoints { get; set; }
    public bool StoryPointsFieldAvailable { get; set; }
}

/// <summary>A row in the round's ticket queue (lightweight; full detail fetched on select). See #38.</summary>
public class QueuedTicket
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Status { get; set; }
    public double? StoryPoints { get; set; }
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// The estimation payload of a poker <see cref="Room"/> (#19) — everything that used to sit on
/// <c>Session</c> but only means something to Planning Poker: the round phase, the deck, the item
/// under discussion, the round timer, the linked ticket/queue and the completed-round history.
/// <para>
/// One-to-one with its room and keyed by <see cref="RoomId"/> (a shared primary key), so a room and
/// its round are always loaded and deleted together.
/// </para>
/// </summary>
public class PokerRound
{
    /// <summary>Primary key, shared with the owning <see cref="Room"/>.</summary>
    public Guid RoomId { get; set; }

    public Room? Room { get; set; }

    public DeckType DeckType { get; set; } = DeckType.Fibonacci;

    /// <summary>For <see cref="DeckType.Custom"/>: the comma-separated card values supplied at creation.</summary>
    public string? CustomCards { get; set; }

    public SessionState State { get; set; } = SessionState.Voting;

    /// <summary>When true, the round auto-reveals once every voter has voted. See #18.</summary>
    public bool AutoReveal { get; set; }

    /// <summary>Optional current story/title being estimated.</summary>
    public string? CurrentStory { get; set; }

    /// <summary>Optional free-text note/justification for the current story (collaborative). See #10.</summary>
    public string? CurrentStoryNote { get; set; }

    /// <summary>The issue tracker this round is connected to, or null. See #4.</summary>
    public IntegrationProvider? LinkedProvider { get; set; }

    /// <summary>The linked ticket, or null if none. Owned entity (same table). See #20.</summary>
    public LinkedIssue? LinkedIssue { get; set; }

    /// <summary>The loaded ticket queue (from a board/query URL or ID list). Persisted as JSON. See #38.</summary>
    public List<QueuedTicket> TicketQueue { get; set; } = new();

    /// <summary>Configured round-timer length in seconds, or null if no timer is configured. See #14.</summary>
    public int? TimerDurationSeconds { get; set; }

    /// <summary>UTC instant the running timer expires; null when idle/paused. See #14.</summary>
    public DateTimeOffset? TimerDeadline { get; set; }

    /// <summary>Seconds left while the timer is paused; null when running/idle. See #14.</summary>
    public int? TimerPausedRemainingSeconds { get; set; }

    /// <summary>Completed estimation rounds, oldest first — the history for analytics (#11).</summary>
    public List<RoundResult> RoundResults { get; set; } = new();

    /// <summary>
    /// Cards/stats stay visible through the discussion phase too — it is a post-reveal phase (#9).
    /// </summary>
    public bool IsRevealed => State is SessionState.Revealed or SessionState.Discussion;
}
