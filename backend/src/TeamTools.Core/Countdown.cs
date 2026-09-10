namespace TeamTools.Core;

/// <summary>
/// The deadline-broadcast primitive both tools' countdowns are built on (#34): a configured length,
/// clamped, turned into one server-authoritative instant every client ticks against locally.
/// <para>
/// Room-level rather than per-tool because it is the *mechanism* that is shared, not the policy. The
/// bounds are the caller's — poker allows a 5-second round timer, a retro phase will not go below 30
/// — and so is what expiry then does: poker force-reveals, a retro only clears the countdown and
/// leaves the phase to the facilitator (#23). Those decisions stay in the tools.
/// </para>
/// </summary>
public static class Countdown
{
    /// <summary>
    /// Clamps a requested length into <paramref name="min"/>..<paramref name="max"/>; null stays
    /// null, meaning "no countdown".
    /// </summary>
    public static int? Normalize(int? seconds, int min, int max) =>
        seconds is null ? null : Math.Clamp(seconds.Value, min, max);

    /// <summary>
    /// The instant a countdown of <paramref name="seconds"/> started at <paramref name="now"/> ends;
    /// null for no countdown. One instant on the wire, so every client agrees when time is up
    /// without trusting its own clock.
    /// </summary>
    public static DateTimeOffset? DeadlineFrom(int? seconds, DateTimeOffset now) =>
        seconds is null ? null : now.AddSeconds(seconds.Value);
}
