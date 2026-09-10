using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// Dot voting (#25): the budget (enforced server-side, never from a client count), the anchoring
/// rule that hides totals while voting is open, and the ranking that orders the discussion.
/// </summary>
public class RetroVotingTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly RetroService _sut;

    public RetroVotingTests()
    {
        var rooms = TestServices.Rooms(_store, new StubShortCodeGenerator(Code), _clock);
        _sut = new RetroService(_store, rooms, _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Facilitator = "alice";
    private const string Bob = "bob";

    private Guid[] _cards = [];

    /// <summary>Seeds three cards and walks the board to the Vote phase.</summary>
    private async Task SeedVotingAsync(int budget = RetroBoard.DefaultVoteBudget, bool stacking = false)
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));

        var board = (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
        var column = board.Columns[0].Id;

        var ids = new List<Guid>();
        foreach (var text in new[] { "CI is slow", "flaky tests", "good docs" })
        {
            var added = await _sut.AddCardAsync(Code, Facilitator, column, text);
            ids.Add(added.Board!.Columns[0].Cards.Last().Id);
        }
        _cards = ids.ToArray();

        await _sut.SetVoteBudgetAsync(Code, Facilitator, budget, stacking);
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Group
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Vote
    }

    private async Task<RetroBoardSnapshot> BoardAsync(string forUserId = Facilitator) =>
        (await _sut.GetByShortCodeAsync(Code, forUserId))!;

    private static RetroCardInfo Card(RetroBoardSnapshot board, Guid id) =>
        board.Columns.SelectMany(c => c.Cards).Single(c => c.Id == id);

    // --- Spending dots -----------------------------------------------------

    [Fact]
    public async Task A_participant_can_spend_a_dot_on_a_card()
    {
        await SeedVotingAsync();

        var result = await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        result.Status.Should().Be(RetroActionStatus.Ok);
        Card(result.Board!, _cards[0]).MyDots.Should().Be(1);
    }

    [Fact]
    public async Task The_snapshot_reports_how_many_dots_this_voter_has_left()
    {
        await SeedVotingAsync(budget: 3);

        (await BoardAsync(Bob)).MyDotsRemaining.Should().Be(3);
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);
        (await BoardAsync(Bob)).MyDotsRemaining.Should().Be(2);
    }

    [Fact]
    public async Task Spending_more_than_the_budget_is_refused_server_side()
    {
        // Never trust a client dot count — the check counts the stored rows.
        await SeedVotingAsync(budget: 2);
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[1]);

        var result = await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[2]);

        result.Status.Should().Be(RetroActionStatus.OutOfDots);
    }

    [Fact]
    public async Task Stacking_dots_on_one_item_is_refused_by_default()
    {
        // Spreading dots surfaces more of what the team cares about.
        await SeedVotingAsync(stacking: false);
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        var result = await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        result.Status.Should().Be(RetroActionStatus.AlreadyVotedForItem);
    }

    [Fact]
    public async Task Stacking_can_be_allowed_by_the_facilitator()
    {
        await SeedVotingAsync(budget: 3, stacking: true);

        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);
        var result = await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        result.Status.Should().Be(RetroActionStatus.Ok);
        Card(result.Board!, _cards[0]).MyDots.Should().Be(2);
    }

    [Fact]
    public async Task Each_voter_gets_their_own_budget()
    {
        await SeedVotingAsync(budget: 1);
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        var result = await _sut.CastVoteAsync(Code, Facilitator, RetroVoteTarget.Card, _cards[0]);

        result.Status.Should().Be(RetroActionStatus.Ok);
    }

    [Fact]
    public async Task Voting_for_an_item_that_is_not_on_the_board_is_refused()
    {
        await SeedVotingAsync();

        var card = await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, Guid.NewGuid());
        var group = await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Group, Guid.NewGuid());

        card.Status.Should().Be(RetroActionStatus.CardNotFound);
        group.Status.Should().Be(RetroActionStatus.GroupNotFound);
    }

    [Fact]
    public async Task The_budget_is_clamped_into_the_allowed_range()
    {
        await SeedVotingAsync();

        await _sut.SetVoteBudgetAsync(Code, Facilitator, 9999, false);

        (await BoardAsync()).VoteBudget.Should().Be(RetroService.MaxVoteBudget);
    }

    [Fact]
    public async Task A_participant_cannot_change_the_budget()
    {
        await SeedVotingAsync();

        var result = await _sut.SetVoteBudgetAsync(Code, Bob, 10, false);

        result.Status.Should().Be(RetroActionStatus.NotOrganiser);
    }

    // --- Taking dots back --------------------------------------------------

    [Fact]
    public async Task A_voter_can_take_a_dot_back()
    {
        await SeedVotingAsync();
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        var result = await _sut.WithdrawVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        result.Status.Should().Be(RetroActionStatus.Ok);
        Card(result.Board!, _cards[0]).MyDots.Should().Be(0);
        result.Board!.MyDotsRemaining.Should().Be(RetroBoard.DefaultVoteBudget);
    }

    [Fact]
    public async Task Withdrawing_a_dot_that_was_never_spent_is_refused()
    {
        await SeedVotingAsync();

        var result = await _sut.WithdrawVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        result.Status.Should().Be(RetroActionStatus.NoVoteToWithdraw);
    }

    [Fact]
    public async Task A_voter_can_only_withdraw_their_own_dot()
    {
        await SeedVotingAsync();
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        var result = await _sut.WithdrawVoteAsync(Code, Facilitator, RetroVoteTarget.Card, _cards[0]);

        result.Status.Should().Be(RetroActionStatus.NoVoteToWithdraw);
        (await BoardAsync(Bob)).Columns.SelectMany(c => c.Cards)
            .Single(c => c.Id == _cards[0]).MyDots.Should().Be(1);
    }

    // --- Hiding totals while voting ----------------------------------------

    [Fact]
    public async Task Totals_are_not_sent_while_voting_is_open()
    {
        // Same anchoring argument as hidden collection: a running total tells people where to put
        // their remaining dots.
        await SeedVotingAsync();
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        var board = await BoardAsync();

        board.VoteTotalsVisible.Should().BeFalse();
        Card(board, _cards[0]).TotalDots.Should().BeNull();
        board.Ranking.Should().BeEmpty("a ranking is a total by another name");
    }

    [Fact]
    public async Task My_own_dots_are_always_visible_to_me()
    {
        await SeedVotingAsync();
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        Card(await BoardAsync(Bob), _cards[0]).MyDots.Should().Be(1);
    }

    [Fact]
    public async Task Another_voters_dots_are_not_disclosed_as_mine()
    {
        await SeedVotingAsync();
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        Card(await BoardAsync(Facilitator), _cards[0]).MyDots.Should().Be(0);
    }

    [Fact]
    public async Task Totals_appear_once_the_discussion_starts()
    {
        await SeedVotingAsync();
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);
        await _sut.CastVoteAsync(Code, Facilitator, RetroVoteTarget.Card, _cards[0]);

        await _sut.AdvancePhaseAsync(Code, Facilitator); // Vote → Discuss

        var board = await BoardAsync();
        board.VoteTotalsVisible.Should().BeTrue();
        Card(board, _cards[0]).TotalDots.Should().Be(2);
    }

    // --- The ranking -------------------------------------------------------

    [Fact]
    public async Task The_discussion_agenda_is_ranked_by_dots()
    {
        await SeedVotingAsync(budget: 3, stacking: true);
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[1]);
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[1]);
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var ranking = (await BoardAsync()).Ranking;

        ranking.Select(r => r.Label).Should().Equal("flaky tests", "CI is slow", "good docs");
        ranking.Select(r => r.Dots).Should().Equal(2, 1, 0);
    }

    [Fact]
    public async Task Ties_break_on_display_order_so_the_ranking_is_stable()
    {
        await SeedVotingAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var ranking = (await BoardAsync()).Ranking;

        ranking.Select(r => r.Label).Should().Equal("CI is slow", "flaky tests", "good docs");
    }

    // --- Grouping and dots -------------------------------------------------

    [Fact]
    public async Task A_themes_total_sums_the_dots_on_the_cards_inside_it()
    {
        // This is why grouping does not have to move any vote rows: a theme's total is its own dots
        // plus its cards'. Grouping sums them; ungrouping hands each card its own back.
        await SeedVotingAsync();
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);
        await _sut.CastVoteAsync(Code, Facilitator, RetroVoteTarget.Card, _cards[1]);

        // Step back to Group (#23 allows one step), gather the two voted cards, then forward again.
        await _sut.PreviousPhaseAsync(Code, Facilitator);
        var grouped = await _sut.GroupCardsAsync(Code, Facilitator, [_cards[0], _cards[1]], null);
        var groupId = grouped.Board!.Groups[0].Id;
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Vote
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Discuss

        var board = await BoardAsync();
        board.Groups.Single(g => g.Id == groupId).TotalDots.Should().Be(2);
    }

    [Fact]
    public async Task Ungrouping_returns_each_card_its_own_dots()
    {
        await SeedVotingAsync();
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);
        await _sut.PreviousPhaseAsync(Code, Facilitator);
        await _sut.GroupCardsAsync(Code, Facilitator, [_cards[0], _cards[1]], null);

        await _sut.UngroupCardAsync(Code, Facilitator, _cards[0]);
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var board = await BoardAsync();
        Card(board, _cards[0]).TotalDots.Should().Be(1, "the dot never left the card");
    }

    [Fact]
    public async Task A_theme_can_be_voted_on_directly()
    {
        await SeedVotingAsync();
        await _sut.PreviousPhaseAsync(Code, Facilitator);
        var grouped = await _sut.GroupCardsAsync(Code, Facilitator, [_cards[0], _cards[1]], null);
        var groupId = grouped.Board!.Groups[0].Id;
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var result = await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Group, groupId);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Groups.Single(g => g.Id == groupId).MyDots.Should().Be(1);
    }

    [Fact]
    public async Task Cards_inside_a_theme_are_not_listed_separately_in_the_ranking()
    {
        // The theme is the unit of discussion; listing its cards too would double-count.
        await SeedVotingAsync();
        await _sut.PreviousPhaseAsync(Code, Facilitator);
        await _sut.GroupCardsAsync(Code, Facilitator, [_cards[0], _cards[1]], null);
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var ranking = (await BoardAsync()).Ranking;

        ranking.Should().HaveCount(2, "one theme plus the one loose card");
        ranking.Select(r => r.Kind).Should().Contain(RetroVoteTarget.Group);
    }

    // --- Phase gating ------------------------------------------------------

    [Fact]
    public async Task Voting_is_refused_before_the_vote_phase()
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        var board = (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
        var added = await _sut.AddCardAsync(Code, Facilitator, board.Columns[0].Id, "a card");
        var cardId = added.Board!.Columns[0].Cards[0].Id;

        var result = await _sut.CastVoteAsync(Code, Facilitator, RetroVoteTarget.Card, cardId);

        result.Status.Should().Be(RetroActionStatus.WrongPhase);
    }

    [Fact]
    public async Task Voting_is_refused_once_the_discussion_starts()
    {
        // The dots are the agenda by then; adding one mid-discussion would reorder it underfoot.
        await SeedVotingAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Discuss

        var result = await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        result.Status.Should().Be(RetroActionStatus.WrongPhase);
    }

    [Fact]
    public async Task Withdrawing_is_refused_outside_the_vote_phase()
    {
        await SeedVotingAsync();
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var result = await _sut.WithdrawVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        result.Status.Should().Be(RetroActionStatus.WrongPhase);
    }

    [Fact]
    public async Task Voting_is_refused_on_a_closed_board()
    {
        await SeedVotingAsync();
        await _sut.CloseBoardAsync(Code, Facilitator);

        var result = await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, _cards[0]);

        result.Status.Should().Be(RetroActionStatus.BoardClosed);
    }
}
