using TeamTools.Core.Models;

namespace TeamTools.Core.Retro;

/// <summary>
/// Counts dots (#25) — the retro sibling of <c>StatsCalculator</c>. Pure and deterministic, so the
/// ranking that orders the discussion can be tested exhaustively without a store.
/// </summary>
public static class RetroTallyCalculator
{
    /// <summary>How many dots this voter has spent in total, across every item.</summary>
    public static int SpentBy(RetroBoard board, string voterUserId) =>
        board.Votes.Count(v => v.VoterUserId == voterUserId);

    /// <summary>Dots this voter has left to spend.</summary>
    public static int RemainingFor(RetroBoard board, string voterUserId) =>
        Math.Max(0, board.VoteBudget - SpentBy(board, voterUserId));

    /// <summary>This voter's dots on one item — what the UI shows back to them while voting.</summary>
    public static int MineOn(RetroBoard board, string voterUserId, RetroVoteTarget kind, Guid targetId) =>
        board.Votes.Count(v => v.VoterUserId == voterUserId && v.TargetKind == kind && v.TargetId == targetId);

    /// <summary>Every dot on one card, from everyone.</summary>
    public static int TotalOnCard(RetroBoard board, Guid cardId) =>
        board.Votes.Count(v => v.TargetKind == RetroVoteTarget.Card && v.TargetId == cardId);

    /// <summary>
    /// Every dot on a theme: the dots spent on the theme itself **plus** the dots on the cards
    /// inside it.
    /// <para>
    /// This is what makes grouping and ungrouping behave sensibly without moving any rows.
    /// Grouping cards that already carry dots sums them into the theme's total; ungrouping hands
    /// each card its own dots back, because they never left. A facilitator who steps back from Vote
    /// to Group (#23 allows one step) can therefore regroup without silently destroying votes.
    /// </para>
    /// </summary>
    public static int TotalOnGroup(RetroBoard board, Guid groupId)
    {
        var onGroup = board.Votes.Count(v => v.TargetKind == RetroVoteTarget.Group && v.TargetId == groupId);
        var cardIds = board.Cards.Where(c => c.GroupId == groupId).Select(c => c.Id).ToHashSet();
        var onCards = board.Votes.Count(v => v.TargetKind == RetroVoteTarget.Card && cardIds.Contains(v.TargetId));
        return onGroup + onCards;
    }

    /// <summary>
    /// The discussion agenda: every votable item — themes, and cards not in a theme — ranked by
    /// dots, highest first. Ties break on the item's own display order so the ranking is stable
    /// rather than arbitrary.
    /// </summary>
    public static IReadOnlyList<(RetroVoteTarget Kind, Guid Id, string Label, int Dots)> Ranking(
        RetroBoard board)
    {
        var themes = board.Groups.Select(g => (
            Kind: RetroVoteTarget.Group,
            Id: g.Id,
            Label: g.Label,
            Dots: TotalOnGroup(board, g.Id),
            Order: g.Order));

        var loose = board.Cards
            .Where(c => c.GroupId is null)
            .Select(c => (
                Kind: RetroVoteTarget.Card,
                Id: c.Id,
                Label: c.Text,
                Dots: TotalOnCard(board, c.Id),
                Order: c.Order));

        return themes
            .Concat(loose)
            .OrderByDescending(x => x.Dots)
            .ThenBy(x => x.Order)
            .Select(x => (x.Kind, x.Id, x.Label, x.Dots))
            .ToArray();
    }
}
