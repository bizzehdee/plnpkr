namespace TeamTools.Core;

/// <summary>
/// One dot, spent by one participant on one item. Both tools that use dot voting store a row per
/// dot rather than a count, which is what lets <see cref="DotBudget"/> answer every question by
/// counting rows — no arithmetic to get wrong, and no client-supplied total to be believed.
/// </summary>
public interface IDotVote
{
    /// <summary>Who spent it. Never broadcast to other participants — only tallies are (#25).</summary>
    string VoterUserId { get; }

    /// <summary>What it was spent on.</summary>
    Guid TargetId { get; }
}

/// <summary>Why a dot could not be spent, or Ok.</summary>
public enum DotSpendStatus
{
    Ok,
    /// <summary>The voter has spent their whole allowance.</summary>
    OutOfDots,
    /// <summary>Already voted for this item, and the room does not allow stacking.</summary>
    AlreadyVotedForItem,
}

/// <summary>
/// Dot-voting budget and tallies (#25), shared by the retro and Lean Coffee (#35).
/// <para>
/// Room-level because the rule is the same wherever dot voting appears and it is a rule worth having
/// in exactly one place: <b>the budget is enforced from the stored rows, never from a count the
/// client sent</b>. What differs per tool is what a dot may be spent *on* — a retro allows a theme
/// or a loose card, a Lean Coffee only a topic — so the tool filters its own rows and hands them in.
/// </para>
/// <para>
/// Generic over the vote row rather than owning a shared table: the two tools' votes live in their
/// own tables and neither references the other (the platform's one structural rule), while the
/// counting lives here once.
/// </para>
/// </summary>
public static class DotBudget
{
    /// <summary>How many dots this voter has spent in total, across every item.</summary>
    public static int SpentBy<TVote>(IEnumerable<TVote> votes, string voterUserId)
        where TVote : IDotVote =>
        votes.Count(v => v.VoterUserId == voterUserId);

    /// <summary>Dots this voter has left. Never negative, even if the budget was lowered mid-vote.</summary>
    public static int RemainingFor<TVote>(IEnumerable<TVote> votes, int budget, string voterUserId)
        where TVote : IDotVote =>
        Math.Max(0, budget - SpentBy(votes, voterUserId));

    /// <summary>This voter's dots on one item — what the UI shows back to them while voting.</summary>
    public static int MineOn<TVote>(IEnumerable<TVote> votes, string voterUserId, Guid targetId)
        where TVote : IDotVote =>
        votes.Count(v => v.VoterUserId == voterUserId && v.TargetId == targetId);

    /// <summary>Every dot on one item, from everyone.</summary>
    public static int TotalOn<TVote>(IEnumerable<TVote> votes, Guid targetId)
        where TVote : IDotVote =>
        votes.Count(v => v.TargetId == targetId);

    /// <summary>
    /// Whether this voter may spend a dot on this item, given the whole set of their room's votes.
    /// <para>
    /// Two rules, both counted from the rows: the allowance, and — unless the room allows stacking —
    /// one dot per item. Spreading dots surfaces more of what the team cares about, which is the
    /// point of dot voting, so stacking is off by default.
    /// </para>
    /// </summary>
    public static DotSpendStatus CanSpend<TVote>(
        IEnumerable<TVote> votes,
        int budget,
        string voterUserId,
        Guid targetId,
        bool allowMultiplePerItem)
        where TVote : IDotVote
    {
        var all = votes as IReadOnlyCollection<TVote> ?? votes.ToList();

        if (RemainingFor(all, budget, voterUserId) <= 0)
        {
            return DotSpendStatus.OutOfDots;
        }

        if (!allowMultiplePerItem && MineOn(all, voterUserId, targetId) > 0)
        {
            return DotSpendStatus.AlreadyVotedForItem;
        }

        return DotSpendStatus.Ok;
    }

    /// <summary>
    /// Items ranked by dots, highest first, ties broken on the caller's display order so the
    /// ranking is stable rather than arbitrary. The agenda a room works through.
    /// </summary>
    public static IReadOnlyList<T> Rank<T>(
        IEnumerable<T> items, Func<T, int> dots, Func<T, int> order) =>
        items.OrderByDescending(dots).ThenBy(order).ToArray();
}
