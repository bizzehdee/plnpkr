using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Core.Models;

/// <summary>
/// The ticket a session is linked to (broadcast-safe content; may persist). The connection/token
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

/// <summary>A row in the session's ticket queue (lightweight; full detail fetched on select). See #38.</summary>
public class QueuedTicket
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Status { get; set; }
    public double? StoryPoints { get; set; }
    public string Url { get; set; } = string.Empty;
}

/// <summary>A planning-poker room. Identified externally by <see cref="ShortCode"/> for invite links. See #1.</summary>
public class Session
{
    public Guid Id { get; set; }

    /// <summary>Short, URL-friendly slug used in the invite link (e.g. "blue-fox-42"). Unique. See #1.</summary>
    public string ShortCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DeckType DeckType { get; set; } = DeckType.Fibonacci;

    /// <summary>For <see cref="DeckType.Custom"/>: the comma-separated card values supplied at creation.</summary>
    public string? CustomCards { get; set; }

    public SessionState State { get; set; } = SessionState.Voting;

    /// <summary>The organiser's UserId, or null if the session has no organiser. See #10.</summary>
    public string? OrganiserUserId { get; set; }

    /// <summary>When true, the session auto-reveals once every voter has voted. See #18.</summary>
    public bool AutoReveal { get; set; }

    /// <summary>
    /// Optional join password, stored as a salted KDF hash (never plaintext). Null = no password. See #2.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>Optional current story/title being estimated.</summary>
    public string? CurrentStory { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>The issue tracker this session is connected to, or null. See #4.</summary>
    public IntegrationProvider? LinkedProvider { get; set; }

    /// <summary>The linked ticket, or null if none. Owned entity (same table). See #20.</summary>
    public LinkedIssue? LinkedIssue { get; set; }

    /// <summary>The loaded ticket queue (from a board/query URL or ID list). Persisted as JSON. See #38.</summary>
    public List<QueuedTicket> TicketQueue { get; set; } = new();

    /// <summary>Whether emoji reactions are currently allowed (organiser-toggleable). See #17.</summary>
    public bool ReactionsEnabled { get; set; } = true;

    /// <summary>Whether participants may switch their own role mid-session (organiser-toggleable). See #21.</summary>
    public bool AllowRoleChange { get; set; } = true;

    /// <summary>Configured round-timer length in seconds, or null if no timer is configured. See #14.</summary>
    public int? TimerDurationSeconds { get; set; }

    /// <summary>UTC instant the running timer expires; null when idle/paused. See #14.</summary>
    public DateTimeOffset? TimerDeadline { get; set; }

    /// <summary>Seconds left while the timer is paused; null when running/idle. See #14.</summary>
    public int? TimerPausedRemainingSeconds { get; set; }

    /// <summary>When set, the session is closed (read-only). See #26.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>When set, the session is soft-deleted (hidden everywhere). See #26.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public List<Participant> Participants { get; set; } = new();
}
