namespace TeamTools.Core;

/// <summary>
/// Tunable limits guarding against abuse of the anonymous, public session model (#3-abuse).
/// Bound in the composition root from configuration ("Abuse:*"); Core tests use the defaults.
/// </summary>
public sealed class SessionLimits
{
    /// <summary>Maximum participants a single session may hold. New joiners beyond this are rejected.</summary>
    public int MaxParticipants { get; init; } = 100;
}
