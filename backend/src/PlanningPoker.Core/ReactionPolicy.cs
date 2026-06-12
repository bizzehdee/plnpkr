namespace PlanningPoker.Core;

/// <summary>
/// The allowlist of emoji that may be broadcast as ephemeral reactions (#17). Reactions are
/// chat-like — never persisted or put in the snapshot — so this only guards what can be sent, keeping
/// arbitrary/large content out of the broadcast. Pure and unit-tested.
/// </summary>
public static class ReactionPolicy
{
    /// <summary>The emoji participants may react with (also the set the UI offers).</summary>
    public static readonly IReadOnlyList<string> Allowed = new[]
    {
        "👍", "👎", "🎉", "😂", "🤔", "❤️", "🚀", "👀",
    };

    private static readonly HashSet<string> AllowedSet = new(Allowed, StringComparer.Ordinal);

    /// <summary>True if <paramref name="emoji"/> is on the allowlist and may be broadcast.</summary>
    public static bool IsAllowed(string? emoji) => emoji is not null && AllowedSet.Contains(emoji);
}
