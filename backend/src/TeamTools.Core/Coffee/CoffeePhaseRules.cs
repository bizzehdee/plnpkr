using TeamTools.Core.Models;

namespace TeamTools.Core.Coffee;

/// <summary>
/// The Lean Coffee phase machine (#35), kept pure so every transition and gate is exhaustively
/// testable without a store.
/// <para>
/// <c>Propose → Vote → Discuss → Done</c>, on the shared <see cref="PhaseRail{TPhase}"/> — one step
/// at a time, back as well as forward, no jumps. The rail is the retro's, generalised; the phases
/// and the gates below are this tool's.
/// </para>
/// </summary>
public static class CoffeePhaseRules
{
    /// <summary>
    /// Bounds for a timebox (seconds). The floor is lower than the retro's 30s because a Lean
    /// Coffee timebox is per topic and five minutes is the usual default — but a facilitator running
    /// a fast round may legitimately want one minute.
    /// </summary>
    public const int MinTimeboxSeconds = 60;
    public const int MaxTimeboxSeconds = 3600;

    /// <summary>The default timebox: five minutes per topic, the Lean Coffee convention.</summary>
    public const int DefaultTimeboxSeconds = 300;

    public static readonly PhaseRail<CoffeePhase> Rail = new(
        CoffeePhase.Propose,
        CoffeePhase.Vote,
        CoffeePhase.Discuss,
        CoffeePhase.Done);

    public static CoffeePhase? Next(CoffeePhase phase) => Rail.Next(phase);

    public static CoffeePhase? Previous(CoffeePhase phase) => Rail.Previous(phase);

    public static bool IsLegalTransition(CoffeePhase from, CoffeePhase to) =>
        Rail.IsLegalTransition(from, to);

    /// <summary>Clamps a requested timebox; null stays null. See <see cref="Countdown"/>.</summary>
    public static int? NormalizeTimebox(int? seconds) =>
        Countdown.Normalize(seconds, MinTimeboxSeconds, MaxTimeboxSeconds);

    /// <summary>
    /// Whether topics may be proposed, reworded or withdrawn. Only during Propose: once the room has
    /// spent dots, a new topic would make the ranking a lie.
    /// </summary>
    public static bool TopicsWritable(CoffeePhase phase) => phase == CoffeePhase.Propose;

    /// <summary>
    /// Whether one participant may read another's topic. Hidden during Propose so nobody writes
    /// "same as Ada's" instead of their own thought — the same anti-anchoring rule as the retro's
    /// hidden collection (#23). An author always sees their own.
    /// </summary>
    public static bool OthersTopicsVisible(CoffeePhase phase) => phase != CoffeePhase.Propose;

    /// <summary>Whether dots may be spent.</summary>
    public static bool VotingAllowed(CoffeePhase phase) => phase == CoffeePhase.Vote;

    /// <summary>
    /// Whether dot totals may be shown. Hidden while voting is open — a running total tells people
    /// where to put their remaining dots — and revealed from Discuss on, which is what orders the
    /// conversation.
    /// </summary>
    public static bool VoteTotalsVisible(CoffeePhase phase) =>
        phase is CoffeePhase.Discuss or CoffeePhase.Done;

    /// <summary>Whether the room is working the list, so a topic can be current and timeboxed.</summary>
    public static bool DiscussionRunning(CoffeePhase phase) => phase == CoffeePhase.Discuss;

    /// <summary>
    /// Whether decisions may be recorded or edited. From Discuss on, and — like the retro's action
    /// items (#26) — still on a closed room, because "we did that" gets ticked days later.
    /// </summary>
    public static bool DecisionsWritable(CoffeePhase phase) =>
        phase is CoffeePhase.Discuss or CoffeePhase.Done;
}
