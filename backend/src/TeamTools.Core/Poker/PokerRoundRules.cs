using TeamTools.Core.Models;

namespace TeamTools.Core.Poker;

/// <summary>
/// Pure round rules (#19). Extracted so the room engine can re-evaluate the auto-reveal gate after a
/// room-level change (a role switch or a disconnect can complete a round) without depending on
/// <see cref="PokerService"/> — see the tool-hook note on <c>RoomService</c>.
/// </summary>
public static class PokerRoundRules
{
    /// <summary>Bounds for a round timer (seconds): at least 5s, at most one hour.</summary>
    public const int MinTimerSeconds = 5;
    public const int MaxTimerSeconds = 3600;

    /// <summary>Clamps a requested timer duration into the allowed range; null stays null (no timer).</summary>
    public static int? NormalizeTimerDuration(int? seconds) =>
        seconds is null ? null : Math.Clamp(seconds.Value, MinTimerSeconds, MaxTimerSeconds);

    /// <summary>Clears the running/paused timer state (idle), leaving the configured duration intact.</summary>
    public static void StopRunningTimer(PokerRound round)
    {
        round.TimerDeadline = null;
        round.TimerPausedRemainingSeconds = null;
    }

    public static void ClearVote(Participant p)
    {
        p.Vote = null;
        p.HasVoted = false;
        p.ChangedAfterReveal = false;
    }

    /// <summary>Auto-reveal fires only while voting, when enabled, and once every voter has voted.</summary>
    public static void MaybeAutoReveal(Room room)
    {
        if (room.PokerRound is not { } round)
        {
            return;
        }

        if (round.State != SessionState.Voting || !round.AutoReveal)
        {
            return;
        }

        // Only connected voters gate the reveal — a voter who dropped mid-round shouldn't block it.
        var voters = room.Participants
            .Where(p => p.Role == ParticipantRole.Voter && p.IsConnected)
            .ToList();
        if (voters.Count > 0 && voters.All(p => p.HasVoted))
        {
            round.State = SessionState.Revealed;
            StopRunningTimer(round); // everyone voted early — drop the countdown
        }
    }
}
