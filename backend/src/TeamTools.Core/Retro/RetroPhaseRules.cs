using TeamTools.Core.Models;

namespace TeamTools.Core.Retro;

/// <summary>
/// The retro phase machine (#23), kept pure so every transition and every gate is exhaustively
/// testable without a store.
/// <para>
/// Phases run forward: <c>Collect → Group → Vote → Discuss → Actions → Closed</c>. Going back is
/// allowed one step at a time and only for an organiser, because facilitators mis-click and the
/// alternative is a retro stuck in the wrong phase. Arbitrary jumps are not allowed: skipping
/// Vote into Discuss would leave the discussion unordered, which is the thing the phases exist to
/// prevent.
/// </para>
/// </summary>
public static class RetroPhaseRules
{
    /// <summary>Bounds for a phase countdown (seconds): at least 30s, at most one hour.</summary>
    public const int MinPhaseSeconds = 30;
    public const int MaxPhaseSeconds = 3600;

    private static readonly RetroPhase[] Order =
    [
        RetroPhase.Collect,
        RetroPhase.Group,
        RetroPhase.Vote,
        RetroPhase.Discuss,
        RetroPhase.Actions,
        RetroPhase.Closed,
    ];

    /// <summary>The phase after this one, or null at the end.</summary>
    public static RetroPhase? Next(RetroPhase phase)
    {
        var i = Array.IndexOf(Order, phase);
        return i >= 0 && i < Order.Length - 1 ? Order[i + 1] : null;
    }

    /// <summary>The phase before this one, or null at the start.</summary>
    public static RetroPhase? Previous(RetroPhase phase)
    {
        var i = Array.IndexOf(Order, phase);
        return i > 0 ? Order[i - 1] : null;
    }

    /// <summary>
    /// Whether a move is a legal transition: exactly one step forward or one step back. Anything
    /// else — a jump, or standing still — is rejected.
    /// </summary>
    public static bool IsLegalTransition(RetroPhase from, RetroPhase to) =>
        Next(from) == to || Previous(from) == to;

    /// <summary>Clamps a requested countdown into the allowed range; null stays null.</summary>
    public static int? NormalizeDuration(int? seconds) =>
        seconds is null ? null : Math.Clamp(seconds.Value, MinPhaseSeconds, MaxPhaseSeconds);

    /// <summary>
    /// Whether cards may be added or edited. Only during <see cref="RetroPhase.Collect"/>: once the
    /// team is grouping and voting, new cards would invalidate the grouping and the tallies behind
    /// them.
    /// </summary>
    public static bool CardsWritable(RetroPhase phase) => phase == RetroPhase.Collect;

    /// <summary>
    /// Whether one participant may see another's card text. Hidden during Collect so nobody anchors
    /// on what has already been written — the retro equivalent of hidden voting (#23). A card's own
    /// author always sees their own.
    /// </summary>
    public static bool OthersCardsVisible(RetroPhase phase) => phase != RetroPhase.Collect;

    /// <summary>Whether cards may be grouped into themes. See #24.</summary>
    public static bool GroupingAllowed(RetroPhase phase) => phase == RetroPhase.Group;

    /// <summary>Whether dots may be spent. See #25.</summary>
    public static bool VotingAllowed(RetroPhase phase) => phase == RetroPhase.Vote;

    /// <summary>
    /// Whether dot totals may be shown. Hidden while voting is open, for the same anchoring reason
    /// hidden collection exists; revealed from Discuss on, which is what orders the discussion.
    /// See #25.
    /// </summary>
    public static bool VoteTotalsVisible(RetroPhase phase) =>
        phase is RetroPhase.Discuss or RetroPhase.Actions or RetroPhase.Closed;

    /// <summary>Whether action items may be added or edited. See #26.</summary>
    public static bool ActionsWritable(RetroPhase phase) =>
        phase is RetroPhase.Discuss or RetroPhase.Actions;
}
