namespace PlanningPoker.Core.Models;

/// <summary>A user taking part in a session. Identified by a stable client-supplied <see cref="UserId"/>. See #8.</summary>
public class Participant
{
    /// <summary>Surrogate key.</summary>
    public int Id { get; set; }

    public Guid SessionId { get; set; }

    /// <summary>Stable per-browser id (localStorage GUID) used to reattach on reconnect. See #34.</summary>
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Lower-cased, trimmed name used for the per-session uniqueness constraint. See #7.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>True for the session creator if they opted to organise. See #10.</summary>
    public bool IsOrganiser { get; set; }

    public ParticipantRole Role { get; set; } = ParticipantRole.Voter;

    /// <summary>The selected card value, or null if not voted / cleared. Hidden from others until revealed.</summary>
    public string? Vote { get; set; }

    /// <summary>True once this participant has an active vote. Always false for observers.</summary>
    public bool HasVoted { get; set; }

    /// <summary>True if the vote was set or changed while the session was Revealed. See #23.</summary>
    public bool ChangedAfterReveal { get; set; }

    /// <summary>
    /// Whether the participant currently has a live connection. A drop sets this false (the seat,
    /// vote, role and organiser status are kept for reconnect); idle eviction removes them later.
    /// See #34.
    /// </summary>
    public bool IsConnected { get; set; } = true;

    /// <summary>Last time the participant connected or disconnected; drives idle eviction.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    public Session? Session { get; set; }
}
