namespace TeamTools.Core;

/// <summary>
/// An ordered, facilitator-driven phase rail (#35): a fixed sequence of phases you may step through
/// one at a time, forwards or back, but never jump around in.
/// <para>
/// Room-level rather than per-tool because it is the *shape* of a facilitated ceremony, not the
/// content of one. A retro runs Collect → Group → Vote → Discuss → Actions → Closed; a Lean Coffee
/// runs Propose → Vote → Discuss → Done. Which phases exist, and what each one permits, stay with
/// the tool — <see cref="Retro.RetroPhaseRules"/> and <c>CoffeePhaseRules</c> declare their own.
/// </para>
/// <para>
/// <b>Why one step at a time.</b> Going back is allowed because facilitators mis-click and the
/// alternative is a room stuck in the wrong phase. Arbitrary jumps are not, because skipping the
/// vote and landing in the discussion leaves it unordered — which is the thing phases exist to
/// prevent. Standing still is not a transition either: a no-op "advance" would broadcast a change
/// that did not happen.
/// </para>
/// <para>
/// Extracted in #35, when Lean Coffee became the second consumer. #34 deliberately left the retro's
/// copy alone rather than generalise from a single example.
/// </para>
/// </summary>
public sealed class PhaseRail<TPhase>
    where TPhase : struct, Enum
{
    private readonly TPhase[] _order;

    public PhaseRail(params TPhase[] order)
    {
        if (order.Length < 2)
        {
            throw new ArgumentException("A rail needs at least two phases.", nameof(order));
        }

        _order = order;
    }

    /// <summary>The phases in order — what a UI renders as the rail.</summary>
    public IReadOnlyList<TPhase> Order => _order;

    /// <summary>The phase after this one, or null at the end of the rail.</summary>
    public TPhase? Next(TPhase phase)
    {
        var i = Array.IndexOf(_order, phase);
        return i >= 0 && i < _order.Length - 1 ? _order[i + 1] : null;
    }

    /// <summary>The phase before this one, or null at the start.</summary>
    public TPhase? Previous(TPhase phase)
    {
        var i = Array.IndexOf(_order, phase);
        return i > 0 ? _order[i - 1] : null;
    }

    /// <summary>
    /// Whether a move is legal: exactly one step forward or one step back. A jump, standing still,
    /// or a phase not on this rail are all rejected.
    /// </summary>
    public bool IsLegalTransition(TPhase from, TPhase to) =>
        (Next(from) is { } next && next.Equals(to))
        || (Previous(from) is { } previous && previous.Equals(to));

    /// <summary>
    /// How far along the rail a phase is, or -1 if it is not on it. Lets a tool answer "have we
    /// passed X yet?" without hard-coding the order a second time.
    /// </summary>
    public int IndexOf(TPhase phase) => Array.IndexOf(_order, phase);

    /// <summary>Whether <paramref name="phase"/> is at or past <paramref name="marker"/>.</summary>
    public bool HasReached(TPhase phase, TPhase marker) =>
        IndexOf(phase) >= 0 && IndexOf(phase) >= IndexOf(marker);
}
