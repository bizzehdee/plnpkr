using TeamTools.Core.Models;

namespace TeamTools.Core;

/// <summary>
/// The rules for "something the room committed to doing" (#26/#35): title validation, and resolving
/// an owner who may or may not be in the room.
/// <para>
/// Room-level because every facilitated ceremony produces them and they behave identically: a retro
/// agrees action items, a Lean Coffee captures decisions against a topic, a standup promotes a
/// blocker to someone to unblock it. The rows still live in each tool's own table — see the note
/// below — but the rules live here once.
/// </para>
/// <para>
/// <b>The owner is free text on purpose.</b> The platform has no accounts, and the person who ends
/// up owning an action may not have been in the room at all. So an owner is either a participant
/// (resolved to their display name, which keeps it current) or whatever was typed.
/// </para>
/// </summary>
public static class ActionItemRules
{
    /// <summary>Longest an action title may be. Long enough for a sentence, short of an essay.</summary>
    public const int MaxTitleLength = 300;

    /// <summary>Longest a free-text owner name may be.</summary>
    public const int MaxOwnerNameLength = 80;

    /// <summary>
    /// The trimmed title, or null if it is blank or too long — the caller turns null into its own
    /// tool's "invalid title" result.
    /// </summary>
    public static string? NormalizeTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        return trimmed.Length == 0 || trimmed.Length > MaxTitleLength ? null : trimmed;
    }

    /// <summary>
    /// How the owner should read: a participant's current display name when
    /// <paramref name="ownerUserId"/> names one, otherwise the typed name, truncated. Null for
    /// unowned — an action nobody has taken yet is a legitimate state, and pretending otherwise
    /// would put a name on it that nobody agreed to.
    /// </summary>
    public static string? ResolveOwnerName(Room room, string? ownerUserId, string? ownerName)
    {
        if (ownerUserId is not null)
        {
            var participant = room.Participants.FirstOrDefault(p => p.UserId == ownerUserId);
            if (participant is not null)
            {
                return participant.DisplayName;
            }
        }

        var trimmed = ownerName?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed[..Math.Min(trimmed.Length, MaxOwnerNameLength)];
    }
}
