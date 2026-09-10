using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// Grouping cards into themes (#24). Twelve cards about slow CI should be one conversation and one
/// vote target, or dot voting splits across duplicates and the real top theme loses.
/// </summary>
public class RetroGroupingTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly RetroService _sut;

    public RetroGroupingTests()
    {
        var rooms = TestServices.Rooms(_store, new StubShortCodeGenerator(Code), _clock);
        _sut = new RetroService(_store, rooms, _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Facilitator = "alice";
    private const string Bob = "bob";

    private Guid _column;

    /// <summary>Seeds a board with three cards and moves it into the Group phase.</summary>
    private async Task<Guid[]> SeedGroupingAsync(params string[] texts)
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));

        var board = (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
        _column = board.Columns[0].Id;

        var ids = new List<Guid>();
        foreach (var text in texts.Length > 0 ? texts : ["CI is slow", "builds take ages", "flaky tests"])
        {
            var added = await _sut.AddCardAsync(Code, Facilitator, _column, text);
            ids.Add(added.Board!.Columns[0].Cards.Last().Id);
        }

        await _sut.AdvancePhaseAsync(Code, Facilitator); // Collect → Group
        return ids.ToArray();
    }

    private async Task<RetroBoardSnapshot> BoardAsync(string forUserId = Facilitator) =>
        (await _sut.GetByShortCodeAsync(Code, forUserId))!;

    // --- Forming a theme ---------------------------------------------------

    [Fact]
    public async Task Grouping_cards_creates_a_theme_holding_them()
    {
        var ids = await SeedGroupingAsync();

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0], ids[1]], null);

        result.Status.Should().Be(RetroActionStatus.Ok);
        var group = result.Board!.Groups.Should().ContainSingle().Subject;
        group.Cards.Select(c => c.Text).Should().BeEquivalentTo("CI is slow", "builds take ages");
    }

    [Fact]
    public async Task A_new_theme_is_named_after_its_first_card()
    {
        // An unnamed theme is harder to discuss than a badly named one; the facilitator renames it.
        var ids = await SeedGroupingAsync();

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0], ids[1]], null);

        result.Board!.Groups[0].Label.Should().Be("CI is slow");
    }

    [Fact]
    public async Task A_long_first_card_is_truncated_into_a_usable_label()
    {
        var ids = await SeedGroupingAsync(new string('x', 400), "second");

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0]], null);

        result.Board!.Groups[0].Label.Should().HaveLength(RetroService.MaxGroupLabelLength);
    }

    [Fact]
    public async Task Cards_carry_their_theme_so_a_client_can_render_the_board_either_way()
    {
        var ids = await SeedGroupingAsync();

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0], ids[1]], null);

        var groupId = result.Board!.Groups[0].Id;
        var inColumn = result.Board.Columns[0].Cards;
        inColumn.Single(c => c.Id == ids[0]).GroupId.Should().Be(groupId);
        inColumn.Single(c => c.Id == ids[2]).GroupId.Should().BeNull();
    }

    [Fact]
    public async Task A_card_can_be_added_to_an_existing_theme()
    {
        var ids = await SeedGroupingAsync();
        var first = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0], ids[1]], null);
        var groupId = first.Board!.Groups[0].Id;

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[2]], groupId);

        result.Board!.Groups.Should().ContainSingle()
            .Which.Cards.Should().HaveCount(3);
    }

    [Fact]
    public async Task Grouping_a_single_card_is_allowed_so_a_standalone_theme_can_be_labelled()
    {
        var ids = await SeedGroupingAsync();

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0]], null);

        result.Board!.Groups.Should().ContainSingle().Which.Cards.Should().ContainSingle();
    }

    [Fact]
    public async Task Grouping_an_unknown_card_is_rejected_without_forming_a_partial_theme()
    {
        var ids = await SeedGroupingAsync();

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0], Guid.NewGuid()], null);

        result.Status.Should().Be(RetroActionStatus.CardNotFound);
        (await BoardAsync()).Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task Grouping_into_an_unknown_theme_is_rejected()
    {
        var ids = await SeedGroupingAsync();

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0]], Guid.NewGuid());

        result.Status.Should().Be(RetroActionStatus.GroupNotFound);
    }

    // --- Moving between themes and out ------------------------------------

    [Fact]
    public async Task Moving_the_last_card_out_of_a_theme_removes_the_empty_theme()
    {
        // An empty theme is not something the team can discuss or vote on.
        var ids = await SeedGroupingAsync();
        await _sut.GroupCardsAsync(Code, Facilitator, [ids[0]], null);

        var result = await _sut.UngroupCardAsync(Code, Facilitator, ids[0]);

        result.Board!.Groups.Should().BeEmpty();
        result.Board.Columns[0].Cards.Single(c => c.Id == ids[0]).GroupId.Should().BeNull();
    }

    [Fact]
    public async Task Moving_a_card_to_another_theme_leaves_the_first_intact_if_cards_remain()
    {
        var ids = await SeedGroupingAsync();
        var a = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0], ids[1]], null);
        var groupA = a.Board!.Groups[0].Id;
        var b = await _sut.GroupCardsAsync(Code, Facilitator, [ids[2]], null);
        var groupB = b.Board!.Groups.Single(g => g.Id != groupA).Id;

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[1]], groupB);

        result.Board!.Groups.Should().HaveCount(2);
        result.Board.Groups.Single(g => g.Id == groupA).Cards.Should().ContainSingle();
        result.Board.Groups.Single(g => g.Id == groupB).Cards.Should().HaveCount(2);
    }

    [Fact]
    public async Task Emptying_a_theme_by_moving_its_cards_away_removes_it_and_renumbers_the_rest()
    {
        var ids = await SeedGroupingAsync();
        var a = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0]], null);
        var groupA = a.Board!.Groups[0].Id;
        var b = await _sut.GroupCardsAsync(Code, Facilitator, [ids[1], ids[2]], null);
        var groupB = b.Board!.Groups.Single(g => g.Id != groupA).Id;

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0]], groupB);

        result.Board!.Groups.Should().ContainSingle().Which.Id.Should().Be(groupB);
        result.Board.Groups[0].Order.Should().Be(0, "orders stay dense after a removal");
    }

    [Fact]
    public async Task Ungrouping_an_unknown_card_is_rejected()
    {
        await SeedGroupingAsync();

        var result = await _sut.UngroupCardAsync(Code, Facilitator, Guid.NewGuid());

        result.Status.Should().Be(RetroActionStatus.CardNotFound);
    }

    // --- Renaming ----------------------------------------------------------

    [Fact]
    public async Task A_theme_can_be_renamed_because_the_name_is_what_the_team_discusses()
    {
        var ids = await SeedGroupingAsync();
        var grouped = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0], ids[1]], null);
        var groupId = grouped.Board!.Groups[0].Id;

        var result = await _sut.RenameGroupAsync(Code, Facilitator, groupId, "  Slow feedback loop  ");

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Groups[0].Label.Should().Be("Slow feedback loop");
    }

    [Fact]
    public async Task A_blank_theme_name_is_rejected()
    {
        var ids = await SeedGroupingAsync();
        var grouped = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0]], null);
        var groupId = grouped.Board!.Groups[0].Id;

        var result = await _sut.RenameGroupAsync(Code, Facilitator, groupId, "   ");

        result.Status.Should().Be(RetroActionStatus.InvalidGroupLabel);
    }

    [Fact]
    public async Task Renaming_an_unknown_theme_is_rejected()
    {
        await SeedGroupingAsync();

        var result = await _sut.RenameGroupAsync(Code, Facilitator, Guid.NewGuid(), "Anything");

        result.Status.Should().Be(RetroActionStatus.GroupNotFound);
    }

    // --- Who may group -----------------------------------------------------

    [Fact]
    public async Task Grouping_is_the_facilitators_job_by_default()
    {
        var ids = await SeedGroupingAsync();

        var result = await _sut.GroupCardsAsync(Code, Bob, [ids[0], ids[1]], null);

        result.Status.Should().Be(RetroActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task A_facilitator_can_open_grouping_to_everyone()
    {
        var ids = await SeedGroupingAsync();
        await _sut.SetAllowParticipantGroupingAsync(Code, Facilitator, true);

        var result = await _sut.GroupCardsAsync(Code, Bob, [ids[0], ids[1]], null);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.AllowParticipantGrouping.Should().BeTrue();
    }

    [Fact]
    public async Task A_participant_cannot_open_grouping_to_everyone()
    {
        await SeedGroupingAsync();

        var result = await _sut.SetAllowParticipantGroupingAsync(Code, Bob, true);

        result.Status.Should().Be(RetroActionStatus.NotOrganiser);
    }

    // --- Phase gating ------------------------------------------------------

    [Fact]
    public async Task Grouping_is_refused_during_collect()
    {
        // Cards are still hidden from each other during Collect, so grouping them is meaningless.
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        var board = (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
        var added = await _sut.AddCardAsync(Code, Facilitator, board.Columns[0].Id, "a card");
        var cardId = added.Board!.Columns[0].Cards[0].Id;

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [cardId], null);

        result.Status.Should().Be(RetroActionStatus.WrongPhase);
    }

    [Fact]
    public async Task Grouping_is_refused_once_voting_starts()
    {
        // Regrouping mid-vote would move the dots people have already spent.
        var ids = await SeedGroupingAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator); // Group → Vote

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0], ids[1]], null);

        result.Status.Should().Be(RetroActionStatus.WrongPhase);
    }

    // --- Interaction with hidden collection and anonymity -----------------

    [Fact]
    public async Task A_themes_cards_are_projected_per_recipient_like_the_columns()
    {
        // Grouping must not become a side door around anonymity (#22): the theme carries the same
        // card projection the columns do.
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true,
            Anonymous: true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
        var board = (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
        var added = await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "said in confidence");
        var cardId = added.Board!.Columns[0].Cards[0].Id;
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.GroupCardsAsync(Code, Facilitator, [cardId], null);

        var themeCard = (await BoardAsync()).Groups[0].Cards[0];

        themeCard.AuthorUserId.Should().BeNull();
        themeCard.AuthorDisplayName.Should().BeNull();
    }

    [Fact]
    public async Task Grouping_is_refused_on_a_closed_board()
    {
        var ids = await SeedGroupingAsync();
        await _sut.CloseBoardAsync(Code, Facilitator);

        var result = await _sut.GroupCardsAsync(Code, Facilitator, [ids[0]], null);

        result.Status.Should().Be(RetroActionStatus.BoardClosed);
    }
}
