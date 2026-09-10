using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// The retro board's foundation (#21): creation from a template, and the card lifecycle every later
/// retro feature operates on.
/// </summary>
public class RetroBoardTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly RetroService _sut;

    public RetroBoardTests()
    {
        var rooms = TestServices.Rooms(_store, new StubShortCodeGenerator(Code), _clock);
        _sut = new RetroService(_store, rooms, _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Facilitator = "alice";
    private const string Bob = "bob";

    private Task<CreateRetroResult> CreateAsync(
        RetroTemplate template = RetroTemplate.WentWellToImprove, string? customColumns = null) =>
        _sut.CreateAsync(new CreateRetroRequest(
            "Sprint 24 retro", template, customColumns, Facilitator, "Alice", Organise: true));

    /// <summary>Alice creates and facilitates; Bob joins as a participant.</summary>
    private async Task<RetroBoardSnapshot> SeedAsync()
    {
        await CreateAsync();
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
        return (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
    }

    private static Guid FirstColumn(RetroBoardSnapshot board) => board.Columns[0].Id;

    private async Task<RetroBoardSnapshot> BoardAsync(string forUserId) =>
        (await _sut.GetByShortCodeAsync(Code, forUserId))!;

    // --- Creation ----------------------------------------------------------

    [Fact]
    public async Task Creating_a_board_materialises_the_templates_columns_in_order()
    {
        var result = await CreateAsync();

        result.Status.Should().Be(CreateRetroStatus.Ok);
        result.Board!.Template.Should().Be(RetroTemplate.WentWellToImprove);
        result.Board.Columns.Select(c => c.Title)
            .Should().Equal("Went well", "To improve", "Action items");
        result.Board.Columns.Select(c => c.Order).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task A_created_board_is_a_retro_room_with_the_creator_seated()
    {
        var result = await CreateAsync();

        result.Board!.Room.Tool.Should().Be(RoomTool.Retro);
        result.Board.Room.Participants.Should().ContainSingle()
            .Which.UserId.Should().Be(Facilitator);
    }

    [Theory]
    [InlineData(RetroTemplate.StartStopContinue, 3)]
    [InlineData(RetroTemplate.FourLs, 4)]
    [InlineData(RetroTemplate.MadSadGlad, 3)]
    public async Task Each_built_in_template_has_its_own_columns(RetroTemplate template, int expected)
    {
        var result = await CreateAsync(template);

        result.Board!.Columns.Should().HaveCount(expected);
    }

    [Fact]
    public async Task A_custom_layout_uses_the_supplied_columns()
    {
        var result = await CreateAsync(RetroTemplate.Custom, "Keep, Drop, Puzzles");

        result.Board!.Columns.Select(c => c.Title).Should().Equal("Keep", "Drop", "Puzzles");
    }

    [Fact]
    public async Task An_empty_custom_layout_is_rejected_rather_than_creating_a_board_with_no_columns()
    {
        var result = await CreateAsync(RetroTemplate.Custom, "   ,  ,");

        result.Status.Should().Be(CreateRetroStatus.InvalidTemplate);
        result.Error.Should().Contain("at least one column");
    }

    [Fact]
    public async Task A_blank_board_name_is_rejected()
    {
        var result = await _sut.CreateAsync(new CreateRetroRequest(
            "  ", RetroTemplate.WentWellToImprove, null, Facilitator, "Alice", Organise: true));

        result.Status.Should().Be(CreateRetroStatus.InvalidName);
    }

    // --- Adding cards ------------------------------------------------------

    [Fact]
    public async Task Any_participant_can_add_a_card_not_just_the_facilitator()
    {
        var board = await SeedAsync();

        var result = await _sut.AddCardAsync(Code, Bob, FirstColumn(board), "Deploys got faster");

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Columns[0].Cards.Should().ContainSingle()
            .Which.Text.Should().Be("Deploys got faster");
    }

    [Fact]
    public async Task Card_text_is_trimmed()
    {
        var board = await SeedAsync();

        await _sut.AddCardAsync(Code, Bob, FirstColumn(board), "   spaced out   ");

        (await BoardAsync(Bob)).Columns[0].Cards[0].Text.Should().Be("spaced out");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_card_is_rejected(string text)
    {
        var board = await SeedAsync();

        var result = await _sut.AddCardAsync(Code, Bob, FirstColumn(board), text);

        result.Status.Should().Be(RetroActionStatus.InvalidCardText);
    }

    [Fact]
    public async Task A_card_longer_than_the_cap_is_rejected()
    {
        var board = await SeedAsync();

        var result = await _sut.AddCardAsync(
            Code, Bob, FirstColumn(board), new string('x', RetroService.MaxCardLength + 1));

        result.Status.Should().Be(RetroActionStatus.InvalidCardText);
    }

    [Fact]
    public async Task A_card_exactly_at_the_cap_is_accepted()
    {
        var board = await SeedAsync();

        var result = await _sut.AddCardAsync(
            Code, Bob, FirstColumn(board), new string('x', RetroService.MaxCardLength));

        result.Status.Should().Be(RetroActionStatus.Ok);
    }

    [Fact]
    public async Task Adding_to_a_column_that_is_not_on_the_board_is_rejected()
    {
        await SeedAsync();

        var result = await _sut.AddCardAsync(Code, Bob, Guid.NewGuid(), "Nowhere to go");

        result.Status.Should().Be(RetroActionStatus.ColumnNotFound);
    }

    [Fact]
    public async Task A_non_participant_cannot_add_a_card()
    {
        var board = await SeedAsync();

        var result = await _sut.AddCardAsync(Code, "stranger", FirstColumn(board), "Let me in");

        result.Status.Should().Be(RetroActionStatus.NotParticipant);
    }

    [Fact]
    public async Task Cards_in_a_column_keep_the_order_they_were_added()
    {
        var board = await SeedAsync();
        var column = FirstColumn(board);

        await _sut.AddCardAsync(Code, Bob, column, "first");
        await _sut.AddCardAsync(Code, Facilitator, column, "second");
        await _sut.AddCardAsync(Code, Bob, column, "third");

        (await BoardAsync(Bob)).Columns[0].Cards.Select(c => c.Text)
            .Should().Equal("first", "second", "third");
    }

    // --- Authorship --------------------------------------------------------

    [Fact]
    public async Task A_card_is_marked_as_mine_only_for_its_author()
    {
        var board = await SeedAsync();
        await _sut.AddCardAsync(Code, Bob, FirstColumn(board), "Bob's card");

        (await BoardAsync(Bob)).Columns[0].Cards[0].IsMine.Should().BeTrue();
        (await BoardAsync(Facilitator)).Columns[0].Cards[0].IsMine.Should().BeFalse();
    }

    [Fact]
    public async Task An_attributed_card_carries_its_authors_name()
    {
        var board = await SeedAsync();
        await _sut.AddCardAsync(Code, Bob, FirstColumn(board), "Bob's card");

        var card = (await BoardAsync(Facilitator)).Columns[0].Cards[0];

        card.AuthorUserId.Should().Be(Bob);
        card.AuthorDisplayName.Should().Be("Bob");
    }

    // --- Editing & deleting ------------------------------------------------

    [Fact]
    public async Task An_author_can_edit_their_own_card()
    {
        var board = await SeedAsync();
        var added = await _sut.AddCardAsync(Code, Bob, FirstColumn(board), "typo");
        var cardId = added.Board!.Columns[0].Cards[0].Id;

        var result = await _sut.EditCardAsync(Code, Bob, cardId, "fixed");

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Columns[0].Cards[0].Text.Should().Be("fixed");
    }

    [Fact]
    public async Task Another_participant_cannot_edit_someone_elses_card()
    {
        var board = await SeedAsync();
        await _sut.JoinAsync(new JoinSessionRequest(Code, "carol", "Carol", ParticipantRole.Voter));
        var added = await _sut.AddCardAsync(Code, Bob, FirstColumn(board), "Bob's words");
        var cardId = added.Board!.Columns[0].Cards[0].Id;

        var result = await _sut.EditCardAsync(Code, "carol", cardId, "rewritten");

        result.Status.Should().Be(RetroActionStatus.NotCardAuthor);
    }

    [Fact]
    public async Task An_organiser_can_moderate_someone_elses_card()
    {
        // Facilitators need to be able to remove something inappropriate or off-topic.
        var board = await SeedAsync();
        var added = await _sut.AddCardAsync(Code, Bob, FirstColumn(board), "off topic");
        var cardId = added.Board!.Columns[0].Cards[0].Id;

        var result = await _sut.DeleteCardAsync(Code, Facilitator, cardId);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Columns[0].Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task Editing_a_card_that_does_not_exist_is_rejected()
    {
        await SeedAsync();

        var result = await _sut.EditCardAsync(Code, Bob, Guid.NewGuid(), "ghost");

        result.Status.Should().Be(RetroActionStatus.CardNotFound);
    }

    [Fact]
    public async Task Deleting_a_card_closes_the_gap_in_the_remaining_order()
    {
        var board = await SeedAsync();
        var column = FirstColumn(board);
        await _sut.AddCardAsync(Code, Bob, column, "first");
        var middle = await _sut.AddCardAsync(Code, Bob, column, "second");
        await _sut.AddCardAsync(Code, Bob, column, "third");

        await _sut.DeleteCardAsync(Code, Bob, middle.Board!.Columns[0].Cards[1].Id);

        var cards = (await BoardAsync(Bob)).Columns[0].Cards;
        cards.Select(c => c.Text).Should().Equal("first", "third");
        cards.Select(c => c.Order).Should().Equal(0, 1);
    }

    // --- Moving ------------------------------------------------------------

    [Fact]
    public async Task Moving_a_card_to_another_column_renumbers_both()
    {
        var board = await SeedAsync();
        var (from, to) = (board.Columns[0].Id, board.Columns[1].Id);
        await _sut.AddCardAsync(Code, Bob, from, "stays");
        var moving = await _sut.AddCardAsync(Code, Bob, from, "moves");
        await _sut.AddCardAsync(Code, Bob, to, "already there");

        var result = await _sut.MoveCardAsync(
            Code, Bob, moving.Board!.Columns[0].Cards[1].Id, to, targetOrder: 0);

        result.Status.Should().Be(RetroActionStatus.Ok);
        var after = await BoardAsync(Bob);
        after.Columns[0].Cards.Select(c => c.Text).Should().Equal("stays");
        after.Columns[0].Cards.Select(c => c.Order).Should().Equal(0);
        after.Columns[1].Cards.Select(c => c.Text).Should().Equal("moves", "already there");
        after.Columns[1].Cards.Select(c => c.Order).Should().Equal(0, 1);
    }

    [Fact]
    public async Task Moving_a_card_within_its_own_column_reorders_it()
    {
        var board = await SeedAsync();
        var column = FirstColumn(board);
        await _sut.AddCardAsync(Code, Bob, column, "a");
        await _sut.AddCardAsync(Code, Bob, column, "b");
        var last = await _sut.AddCardAsync(Code, Bob, column, "c");

        await _sut.MoveCardAsync(Code, Bob, last.Board!.Columns[0].Cards[2].Id, column, targetOrder: 0);

        (await BoardAsync(Bob)).Columns[0].Cards.Select(c => c.Text).Should().Equal("c", "a", "b");
    }

    [Fact]
    public async Task An_out_of_range_target_position_clamps_instead_of_failing()
    {
        var board = await SeedAsync();
        var column = FirstColumn(board);
        var first = await _sut.AddCardAsync(Code, Bob, column, "a");
        await _sut.AddCardAsync(Code, Bob, column, "b");

        var result = await _sut.MoveCardAsync(
            Code, Bob, first.Board!.Columns[0].Cards[0].Id, column, targetOrder: 99);

        result.Status.Should().Be(RetroActionStatus.Ok);
        (await BoardAsync(Bob)).Columns[0].Cards.Select(c => c.Text).Should().Equal("b", "a");
    }

    [Fact]
    public async Task Moving_a_card_to_a_column_that_is_not_on_the_board_is_rejected()
    {
        var board = await SeedAsync();
        var added = await _sut.AddCardAsync(Code, Bob, FirstColumn(board), "here");

        var result = await _sut.MoveCardAsync(
            Code, Bob, added.Board!.Columns[0].Cards[0].Id, Guid.NewGuid(), 0);

        result.Status.Should().Be(RetroActionStatus.ColumnNotFound);
    }

    // --- Template changes --------------------------------------------------

    [Fact]
    public async Task An_organiser_can_change_the_layout_of_an_empty_board()
    {
        await SeedAsync();

        var result = await _sut.SetTemplateAsync(Code, Facilitator, RetroTemplate.FourLs, null);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Columns.Select(c => c.Title)
            .Should().Equal("Liked", "Learned", "Lacked", "Longed for");
    }

    [Fact]
    public async Task Changing_the_layout_is_refused_once_cards_exist()
    {
        // Cards belong to columns. Silently rehoming or dropping them would lose the team's words,
        // so the organiser has to clear the board first.
        var board = await SeedAsync();
        await _sut.AddCardAsync(Code, Bob, FirstColumn(board), "worth keeping");

        var result = await _sut.SetTemplateAsync(Code, Facilitator, RetroTemplate.FourLs, null);

        result.Status.Should().Be(RetroActionStatus.InvalidTemplate);
        (await BoardAsync(Bob)).Columns[0].Cards.Should().ContainSingle();
    }

    [Fact]
    public async Task A_non_organiser_cannot_change_the_layout()
    {
        await SeedAsync();

        var result = await _sut.SetTemplateAsync(Code, Bob, RetroTemplate.FourLs, null);

        result.Status.Should().Be(RetroActionStatus.NotOrganiser);
    }

    // --- Room-level inheritance --------------------------------------------

    [Fact]
    public async Task A_closed_board_rejects_new_cards()
    {
        // Read-only close (#26) is room-level, so the retro tool inherits it rather than
        // implementing its own.
        var board = await SeedAsync();
        await _sut.CloseBoardAsync(Code, Facilitator);

        var result = await _sut.AddCardAsync(Code, Bob, FirstColumn(board), "too late");

        result.Status.Should().Be(RetroActionStatus.BoardClosed);
    }

    [Fact]
    public async Task A_second_participant_cannot_take_a_name_already_in_the_room()
    {
        // Per-room name uniqueness (#7) is room-level and shared.
        await SeedAsync();

        var result = await _sut.JoinAsync(new JoinSessionRequest(Code, "carol", "bob", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.NameTaken);
    }

    [Fact]
    public async Task A_password_protected_board_refuses_a_joiner_without_the_password()
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            "Private retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice",
            Organise: true, Password: "hunter2"));

        var result = await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.PasswordRequired);
    }

    [Fact]
    public async Task Facilitation_passes_to_the_longest_present_participant_when_the_organiser_drops()
    {
        // Auto-succession (#7), inherited from the room engine.
        await SeedAsync();

        await _sut.MarkDisconnectedAsync(Code, Facilitator);

        var board = await BoardAsync(Bob);
        board.Room.Participants.Single(p => p.UserId == Bob).IsOrganiser.Should().BeTrue();
    }

    [Fact]
    public async Task Deleting_a_board_leaves_nothing_to_read()
    {
        await SeedAsync();

        var result = await _sut.DeleteBoardAsync(Code, Facilitator);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board.Should().BeNull("the board is gone — clients are told it closed, not sent a snapshot");
        (await _sut.GetByShortCodeAsync(Code, Facilitator)).Should().BeNull();
    }

    // --- The tool boundary -------------------------------------------------

    [Fact]
    public async Task Retro_operations_refuse_a_room_that_hosts_another_tool()
    {
        var poker = TestServices.Poker(_store, new StubShortCodeGenerator("poker-1"), _clock);
        await poker.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, Facilitator, "Alice", Organise: true));

        var act = () => _sut.AddCardAsync("poker-1", Facilitator, Guid.NewGuid(), "wrong tool");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*hosts Poker, not Retro*");
    }

    [Fact]
    public async Task Reading_an_unknown_board_returns_null()
    {
        (await _sut.GetByShortCodeAsync("no-such-board", Facilitator)).Should().BeNull();
    }
}
