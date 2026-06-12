namespace PlanningPoker.Core.Contracts;

public enum IntegrationStatus
{
    Ok,
    Disabled,
    SessionNotFound,
    NotParticipant,
    NotOrganiser,
    NotConnected,
    AuthFailed,
    IssueNotFound,
    ProviderError,
    /// <summary>The session is closed (read-only). See #26.</summary>
    SessionClosed,
}

/// <summary>
/// Result of an integration action (connect/disconnect/link/submit). <see cref="Session"/> is the
/// post-change snapshot to broadcast on success; <see cref="AccountName"/> is the connected display
/// name (returned to the connecting user only, never broadcast). See #4.
/// </summary>
public record IntegrationResult(
    IntegrationStatus Status,
    SessionSnapshot? Session,
    string? AccountName,
    string? Error)
{
    public static IntegrationResult Ok(SessionSnapshot session, string? accountName = null) =>
        new(IntegrationStatus.Ok, session, accountName, null);

    public static IntegrationResult Disabled() =>
        new(IntegrationStatus.Disabled, null, null, "Issue-tracker integration is disabled.");
    public static IntegrationResult NotFound() => new(IntegrationStatus.SessionNotFound, null, null, "Session not found.");
    public static IntegrationResult NotParticipant() => new(IntegrationStatus.NotParticipant, null, null, "You are not in this session.");
    public static IntegrationResult NotOrganiser() => new(IntegrationStatus.NotOrganiser, null, null, "Only the organiser can manage the integration.");
    public static IntegrationResult NotConnected() => new(IntegrationStatus.NotConnected, null, null, "Connect to a tracker first.");
    public static IntegrationResult SessionClosed() => new(IntegrationStatus.SessionClosed, null, null, "This session is closed.");
    public static IntegrationResult Fail(IntegrationStatus status, string error) => new(status, null, null, error);
}
