using TeamTools.Core.Models;

namespace TeamTools.Core.Contracts;

/// <summary>
/// The room-level half of every broadcast snapshot (#19) — identity, the participant list with
/// presence and organiser flags, and the closed state. Both tools embed this exact record rather
/// than each restating the fields, so a room-level addition is made once and reaches both.
/// </summary>
public record RoomSnapshot(
    Guid Id,
    string ShortCode,
    string Name,
    RoomTool Tool,
    string? OrganiserUserId,
    bool ReactionsEnabled,
    bool AllowRoleChange,
    bool IsClosed,
    bool HasPassword,
    IReadOnlyList<ParticipantInfo> Participants);

/// <summary>
/// Public view of a participant. <see cref="Vote"/> is null while votes are hidden and carries the
/// chosen card once revealed (poker only). <see cref="HasVoted"/> is always visible.
/// </summary>
public record ParticipantInfo(
    string UserId,
    string DisplayName,
    bool IsOrganiser,
    ParticipantRole Role,
    bool HasVoted,
    bool ChangedAfterReveal,
    string? Vote,
    bool IsConnected,
    bool IsOutlier);

/// <summary>Projects the shared room fragment. Tool projectors call this and add their own state.</summary>
public static class RoomProjection
{
    public static RoomSnapshot ToSnapshot(Room room, IReadOnlyList<ParticipantInfo> participants) => new(
        room.Id,
        room.ShortCode,
        room.Name,
        room.Tool,
        room.OrganiserUserId,
        room.ReactionsEnabled,
        room.AllowRoleChange,
        room.IsClosed,
        room.PasswordHash is not null,
        participants);

    /// <summary>
    /// Projects a participant. The vote value and outlier flag are only meaningful when revealed;
    /// <paramref name="outlierValues"/> is the set of card values flagged as outliers (#44).
    /// </summary>
    public static ParticipantInfo ToInfo(Participant p, bool revealed, IReadOnlyList<string>? outlierValues = null) => new(
        p.UserId,
        p.DisplayName,
        p.IsOrganiser,
        p.Role,
        p.HasVoted,
        p.ChangedAfterReveal,
        revealed ? p.Vote : null,
        p.IsConnected,
        revealed && p.Vote is not null && (outlierValues?.Contains(p.Vote) ?? false));

    /// <summary>Participants in join order — the seat order every client renders.</summary>
    public static IReadOnlyList<ParticipantInfo> ToInfos(
        Room room, bool revealed, IReadOnlyList<string>? outlierValues = null) =>
        room.Participants
            .OrderBy(p => p.Id)
            .Select(p => ToInfo(p, revealed, outlierValues))
            .ToArray();
}
