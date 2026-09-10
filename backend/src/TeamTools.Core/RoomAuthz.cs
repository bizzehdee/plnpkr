using TeamTools.Core.Models;

namespace TeamTools.Core;

/// <summary>
/// Who may control a room, and who inherits control when organisers drop (#7, #19). Pure and
/// room-level, so both tools share one answer rather than each deciding for itself.
/// </summary>
public static class RoomAuthz
{
    /// <summary>
    /// True if the user may control the room (reveal/reset/phase/settings). With multiple organisers
    /// (#7) the founding organiser (<see cref="Room.OrganiserUserId"/>) and any participant flagged
    /// <see cref="Participant.IsOrganiser"/> qualify. When the room has no organiser at all, anyone
    /// in it may control — preserving the long-standing "no organiser ⇒ open" rule (#10).
    /// </summary>
    public static bool CanControl(Room room, string userId)
    {
        if (!HasOrganiser(room))
        {
            return true;
        }

        if (room.OrganiserUserId == userId)
        {
            return true;
        }

        var participant = room.Participants.FirstOrDefault(p => p.UserId == userId);
        return participant is { IsOrganiser: true };
    }

    /// <summary>
    /// Auto-succession (#7): if a room that has organisers is left with no <em>connected</em>
    /// organiser, promote the longest-present connected participant so it is never stuck without a
    /// facilitator. No-op for open (no-organiser) rooms or when an organiser is still connected.
    /// </summary>
    public static void PromoteSuccessorIfNeeded(Room room)
    {
        if (!HasOrganiser(room))
        {
            return;
        }

        if (room.Participants.Any(p => p.IsOrganiser && p.IsConnected))
        {
            return;
        }

        // Longest-present connected participant (earliest row) inherits the facilitator role.
        var successor = room.Participants
            .Where(p => p.IsConnected)
            .OrderBy(p => p.Id)
            .FirstOrDefault();
        if (successor is not null)
        {
            successor.IsOrganiser = true;
        }
    }

    private static bool HasOrganiser(Room room) =>
        room.OrganiserUserId is not null || room.Participants.Any(p => p.IsOrganiser);
}
