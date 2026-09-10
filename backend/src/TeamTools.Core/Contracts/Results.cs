using TeamTools.Core.Models;

namespace TeamTools.Core.Contracts;

public enum CreateSessionStatus
{
    Ok,
    InvalidName,
    InvalidDeck,
    /// <summary>The caller is creating sessions too quickly (abuse throttle). See #3-abuse.</summary>
    RateLimited,
}

public record CreateSessionResult(
    CreateSessionStatus Status,
    SessionSnapshot? Session,
    string? Error)
{
    public static CreateSessionResult Ok(SessionSnapshot session) => new(CreateSessionStatus.Ok, session, null);
    public static CreateSessionResult InvalidName(string error) => new(CreateSessionStatus.InvalidName, null, error);
    public static CreateSessionResult InvalidDeck(string error) => new(CreateSessionStatus.InvalidDeck, null, error);
    public static CreateSessionResult RateLimited() =>
        new(CreateSessionStatus.RateLimited, null, "You're doing that too often — please wait a moment and try again.");
}

public enum JoinStatus
{
    Ok,
    SessionNotFound,
    InvalidName,
    NameTaken,
    PasswordRequired,
    WrongPassword,
    SessionClosed,
    /// <summary>The session has reached its participant cap. See #3-abuse.</summary>
    SessionFull,
    /// <summary>The caller is joining too quickly (abuse throttle). See #3-abuse.</summary>
    RateLimited,
}

public record JoinResult(
    JoinStatus Status,
    SessionSnapshot? Session,
    ParticipantInfo? Participant,
    string? Error)
{
    public static JoinResult Ok(SessionSnapshot session, ParticipantInfo participant) =>
        new(JoinStatus.Ok, session, participant, null);

    public static JoinResult NotFound() => new(JoinStatus.SessionNotFound, null, null, "Session not found.");
    public static JoinResult InvalidName(string error) => new(JoinStatus.InvalidName, null, null, error);
    public static JoinResult NameTaken() =>
        new(JoinStatus.NameTaken, null, null, "That name is already taken in this session — please pick another.");
    public static JoinResult PasswordRequired() =>
        new(JoinStatus.PasswordRequired, null, null, "This session requires a password.");
    public static JoinResult WrongPassword() =>
        new(JoinStatus.WrongPassword, null, null, "Incorrect password — please try again.");
    public static JoinResult SessionClosed() =>
        new(JoinStatus.SessionClosed, null, null, "This session is closed and can no longer be joined.");
    public static JoinResult SessionFull() =>
        new(JoinStatus.SessionFull, null, null, "This session is full — it has reached its participant limit.");
    public static JoinResult RateLimited() =>
        new(JoinStatus.RateLimited, null, null, "You're doing that too often — please wait a moment and try again.");
}

/// <summary>
/// Lean info for the /join landing page: enough to render it without exposing the full snapshot (#2),
/// plus which tool the short code belongs to so the page can route to /poker or /retro (#19).
/// </summary>
public record SessionLanding(string Name, string ShortCode, bool RequiresPassword, RoomTool Tool);

public enum LeaveStatus
{
    Ok,
    SessionNotFound,
    NotInSession,
}

/// <summary>Result of a participant leaving. <see cref="Session"/> is the post-leave snapshot.</summary>
public record LeaveResult(LeaveStatus Status, SessionSnapshot? Session)
{
    public static LeaveResult Ok(SessionSnapshot session) => new(LeaveStatus.Ok, session);
    public static LeaveResult NotFound() => new(LeaveStatus.SessionNotFound, null);
    public static LeaveResult NotInSession() => new(LeaveStatus.NotInSession, null);
}
