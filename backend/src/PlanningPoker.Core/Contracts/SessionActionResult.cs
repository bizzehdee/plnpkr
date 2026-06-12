namespace PlanningPoker.Core.Contracts;

public enum SessionActionStatus
{
    Ok,
    SessionNotFound,
    NotParticipant,
    /// <summary>Caller is not the organiser of a session that has one. See #10.</summary>
    NotOrganiser,
    /// <summary>Observers cannot vote. See #8.</summary>
    ObserverCannotVote,
    /// <summary>The submitted card is not part of this session's deck.</summary>
    InvalidCard,
    /// <summary>The reset target is not a participant.</summary>
    TargetNotFound,
    /// <summary>The requested deck is invalid (e.g. an empty custom deck). See #32.</summary>
    InvalidDeck,
    /// <summary>A non-organiser tried to change their role while the organiser has it disabled. See #21.</summary>
    RoleChangeDisabled,
    /// <summary>The session is closed (read-only) and can't be changed. See #26.</summary>
    SessionClosed,
}

/// <summary>
/// Result of a session mutation (vote/reveal/reset/etc). <see cref="Session"/> is the post-change
/// snapshot to broadcast when <see cref="Status"/> is <see cref="SessionActionStatus.Ok"/>.
/// </summary>
public record SessionActionResult(SessionActionStatus Status, SessionSnapshot? Session)
{
    public static SessionActionResult Ok(SessionSnapshot session) => new(SessionActionStatus.Ok, session);
    public static SessionActionResult NotFound() => new(SessionActionStatus.SessionNotFound, null);
    public static SessionActionResult NotParticipant() => new(SessionActionStatus.NotParticipant, null);
    public static SessionActionResult NotOrganiser() => new(SessionActionStatus.NotOrganiser, null);
    public static SessionActionResult ObserverCannotVote() => new(SessionActionStatus.ObserverCannotVote, null);
    public static SessionActionResult InvalidCard() => new(SessionActionStatus.InvalidCard, null);
    public static SessionActionResult TargetNotFound() => new(SessionActionStatus.TargetNotFound, null);
    public static SessionActionResult InvalidDeck() => new(SessionActionStatus.InvalidDeck, null);
    public static SessionActionResult RoleChangeDisabled() => new(SessionActionStatus.RoleChangeDisabled, null);
    public static SessionActionResult SessionClosed() => new(SessionActionStatus.SessionClosed, null);
}
