using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// Action items (#26) — the only retro artefact with a life after the meeting, which is what makes
/// the closed-board carve-out necessary and what makes carry-over (#27) worth having.
/// </summary>
public class RetroActionTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly RetroService _sut;

    public RetroActionTests()
    {
        var rooms = TestServices.Rooms(_store, new StubShortCodeGenerator(Code), _clock);
        _sut = new RetroService(_store, rooms, _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Facilitator = "alice";
    private const string Bob = "bob";

    /// <summary>Seeds a board and walks it to Discuss, where actions become writable.</summary>
    private async Task SeedAsync()
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Group
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Vote
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Discuss
    }

    private async Task<RetroBoardSnapshot> BoardAsync(string forUserId = Facilitator) =>
        (await _sut.GetByShortCodeAsync(Code, forUserId))!;

    private Task<RetroActionResult> AddAsync(
        string title = "Fix the flaky test", string? ownerUserId = null, string? ownerName = null,
        DateTimeOffset? due = null, Guid? sourceGroupId = null) =>
        _sut.AddActionAsync(Code, Facilitator, title, ownerUserId, ownerName, due, sourceGroupId);

    // --- Recording actions -------------------------------------------------

    [Fact]
    public async Task An_action_can_be_recorded_during_the_discussion()
    {
        await SeedAsync();

        var result = await AddAsync();

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Actions.Should().ContainSingle()
            .Which.Title.Should().Be("Fix the flaky test");
    }

    [Fact]
    public async Task Any_participant_can_record_an_action()
    {
        // A retro where only the facilitator can write down a commitment is a retro nobody owns.
        await SeedAsync();

        var result = await _sut.AddActionAsync(Code, Bob, "Book the room earlier", null, null, null, null);

        result.Status.Should().Be(RetroActionStatus.Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_action_without_a_title_is_rejected(string title)
    {
        await SeedAsync();

        var result = await AddAsync(title);

        result.Status.Should().Be(RetroActionStatus.InvalidActionTitle);
    }

    [Fact]
    public async Task An_over_long_action_title_is_rejected()
    {
        await SeedAsync();

        var result = await AddAsync(new string('x', RetroService.MaxActionTitleLength + 1));

        result.Status.Should().Be(RetroActionStatus.InvalidActionTitle);
    }

    [Fact]
    public async Task An_action_can_come_out_of_a_theme()
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        var board = (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
        var added = await _sut.AddCardAsync(Code, Facilitator, board.Columns[0].Id, "CI is slow");
        var cardId = added.Board!.Columns[0].Cards[0].Id;
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        var grouped = await _sut.GroupCardsAsync(Code, Facilitator, [cardId], null);
        var groupId = grouped.Board!.Groups[0].Id;
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var result = await AddAsync("Speed up CI", sourceGroupId: groupId);

        result.Board!.Actions.Should().ContainSingle()
            .Which.SourceGroupId.Should().Be(groupId);
    }

    [Fact]
    public async Task An_action_citing_a_theme_that_is_not_on_the_board_is_rejected()
    {
        await SeedAsync();

        var result = await AddAsync(sourceGroupId: Guid.NewGuid());

        result.Status.Should().Be(RetroActionStatus.GroupNotFound);
    }

    // --- Owners ------------------------------------------------------------

    [Fact]
    public async Task An_action_can_be_left_unowned()
    {
        // Plenty of actions belong to the team rather than to a person.
        await SeedAsync();

        var result = await AddAsync();

        var action = result.Board!.Actions[0];
        action.OwnerUserId.Should().BeNull();
        action.OwnerName.Should().BeNull();
    }

    [Fact]
    public async Task Picking_a_participant_as_owner_stores_their_display_name()
    {
        // Storing the name means the action still reads correctly after they leave the room, or are
        // evicted from it.
        await SeedAsync();

        var result = await AddAsync(ownerUserId: Bob);

        var action = result.Board!.Actions[0];
        action.OwnerUserId.Should().Be(Bob);
        action.OwnerName.Should().Be("Bob");
    }

    [Fact]
    public async Task An_owner_can_be_someone_who_was_never_in_the_room()
    {
        // The platform has no accounts, and the person who ends up owning an action may not have
        // been at the retro at all.
        await SeedAsync();

        var result = await AddAsync(ownerName: "  Dana from Platform  ");

        var action = result.Board!.Actions[0];
        action.OwnerUserId.Should().BeNull();
        action.OwnerName.Should().Be("Dana from Platform");
    }

    [Fact]
    public async Task An_over_long_free_text_owner_is_truncated_rather_than_rejected()
    {
        await SeedAsync();

        var result = await AddAsync(ownerName: new string('x', RetroService.MaxOwnerNameLength + 20));

        result.Board!.Actions[0].OwnerName.Should().HaveLength(RetroService.MaxOwnerNameLength);
    }

    // --- Due dates and done ------------------------------------------------

    [Fact]
    public async Task An_action_can_carry_a_due_date()
    {
        await SeedAsync();
        var due = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        var result = await AddAsync(due: due);

        result.Board!.Actions[0].DueDate.Should().Be(due);
    }

    [Fact]
    public async Task An_action_can_be_marked_done_and_reopened()
    {
        await SeedAsync();
        var added = await AddAsync();
        var actionId = added.Board!.Actions[0].Id;

        var done = await _sut.ToggleActionDoneAsync(Code, Facilitator, actionId);
        done.Board!.Actions[0].IsDone.Should().BeTrue();
        done.Board.Actions[0].DoneAt.Should().Be(_clock.UtcNow);

        var reopened = await _sut.ToggleActionDoneAsync(Code, Facilitator, actionId);
        reopened.Board!.Actions[0].IsDone.Should().BeFalse();
        reopened.Board.Actions[0].DoneAt.Should().BeNull();
    }

    [Fact]
    public async Task Outstanding_actions_are_listed_before_finished_ones()
    {
        // What still needs doing is what the team came back for.
        await SeedAsync();
        var first = await AddAsync("done already");
        await AddAsync("still to do");
        await _sut.ToggleActionDoneAsync(Code, Facilitator, first.Board!.Actions[0].Id);

        var actions = (await BoardAsync()).Actions;

        actions.Select(a => a.Title).Should().Equal("still to do", "done already");
    }

    [Fact]
    public async Task Actions_with_a_due_date_come_before_ones_without()
    {
        await SeedAsync();
        await AddAsync("someday");
        await AddAsync("by Friday", due: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        (await BoardAsync()).Actions.Select(a => a.Title).Should().Equal("by Friday", "someday");
    }

    // --- Editing and deleting ----------------------------------------------

    [Fact]
    public async Task An_action_can_be_reworded_and_reassigned()
    {
        await SeedAsync();
        var added = await AddAsync();
        var actionId = added.Board!.Actions[0].Id;
        var due = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        var result = await _sut.EditActionAsync(
            Code, Facilitator, actionId, "  Quarantine the flaky test  ", Bob, null, due);

        result.Status.Should().Be(RetroActionStatus.Ok);
        var action = result.Board!.Actions[0];
        action.Title.Should().Be("Quarantine the flaky test");
        action.OwnerName.Should().Be("Bob");
        action.DueDate.Should().Be(due);
    }

    [Fact]
    public async Task An_action_can_be_deleted()
    {
        await SeedAsync();
        var added = await AddAsync();

        var result = await _sut.DeleteActionAsync(Code, Facilitator, added.Board!.Actions[0].Id);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Operating_on_an_action_that_does_not_exist_is_rejected()
    {
        await SeedAsync();

        var toggled = await _sut.ToggleActionDoneAsync(Code, Facilitator, Guid.NewGuid());
        var deleted = await _sut.DeleteActionAsync(Code, Facilitator, Guid.NewGuid());

        toggled.Status.Should().Be(RetroActionStatus.ActionNotFound);
        deleted.Status.Should().Be(RetroActionStatus.ActionNotFound);
    }

    [Fact]
    public async Task A_non_participant_cannot_touch_the_actions()
    {
        await SeedAsync();
        await AddAsync();

        var result = await _sut.AddActionAsync(Code, "stranger", "Sneak this in", null, null, null, null);

        result.Status.Should().Be(RetroActionStatus.NotParticipant);
    }

    // --- Phase gating ------------------------------------------------------

    [Fact]
    public async Task Actions_cannot_be_recorded_before_the_discussion()
    {
        // Committing to something before the team has discussed anything is putting the cart first.
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));

        var result = await AddAsync();

        result.Status.Should().Be(RetroActionStatus.WrongPhase);
    }

    [Fact]
    public async Task Actions_can_be_recorded_in_the_actions_phase()
    {
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator); // Discuss → Actions

        var result = await AddAsync();

        result.Status.Should().Be(RetroActionStatus.Ok);
    }

    // --- The closed-board carve-out (the point of this task) ---------------

    [Fact]
    public async Task Actions_stay_writable_on_a_closed_board()
    {
        // "Mark done" happens days after the retro ended. A closed board that cannot record that is
        // a board nobody comes back to — so this is the one deliberate write on a closed room.
        await SeedAsync();
        var added = await AddAsync();
        var actionId = added.Board!.Actions[0].Id;
        await _sut.CloseBoardAsync(Code, Facilitator);

        var done = await _sut.ToggleActionDoneAsync(Code, Facilitator, actionId);

        done.Status.Should().Be(RetroActionStatus.Ok);
        done.Board!.Actions[0].IsDone.Should().BeTrue();
        done.Board.Room.IsClosed.Should().BeTrue("the board is still closed — only actions moved");
    }

    [Fact]
    public async Task A_new_action_can_be_added_to_a_closed_board()
    {
        // Follow-up work gets agreed after the meeting too.
        await SeedAsync();
        await _sut.CloseBoardAsync(Code, Facilitator);

        var result = await AddAsync("Agreed in the hallway afterwards");

        result.Status.Should().Be(RetroActionStatus.Ok);
    }

    [Fact]
    public async Task Actions_can_be_edited_and_deleted_on_a_closed_board()
    {
        await SeedAsync();
        var added = await AddAsync();
        var actionId = added.Board!.Actions[0].Id;
        await _sut.CloseBoardAsync(Code, Facilitator);

        var edited = await _sut.EditActionAsync(
            Code, Facilitator, actionId, "Reworded after the fact", null, null, null);
        edited.Status.Should().Be(RetroActionStatus.Ok);

        var deleted = await _sut.DeleteActionAsync(Code, Facilitator, actionId);
        deleted.Status.Should().Be(RetroActionStatus.Ok);
    }

    [Fact]
    public async Task The_carve_out_covers_actions_and_nothing_else()
    {
        // The whole risk of a carve-out is that it widens. Everything else on a closed board must
        // still be frozen.
        await SeedAsync();
        var board = await BoardAsync();
        await _sut.PreviousPhaseAsync(Code, Facilitator); // → Vote
        await _sut.PreviousPhaseAsync(Code, Facilitator); // → Group
        await _sut.PreviousPhaseAsync(Code, Facilitator); // → Collect
        var added = await _sut.AddCardAsync(Code, Facilitator, board.Columns[0].Id, "a card");
        var cardId = added.Board!.Columns[0].Cards[0].Id;
        await _sut.CloseBoardAsync(Code, Facilitator);

        (await _sut.AddCardAsync(Code, Facilitator, board.Columns[0].Id, "another"))
            .Status.Should().Be(RetroActionStatus.BoardClosed);
        (await _sut.EditCardAsync(Code, Facilitator, cardId, "reworded"))
            .Status.Should().Be(RetroActionStatus.BoardClosed);
        (await _sut.DeleteCardAsync(Code, Facilitator, cardId))
            .Status.Should().Be(RetroActionStatus.BoardClosed);
        (await _sut.GroupCardsAsync(Code, Facilitator, [cardId], null))
            .Status.Should().Be(RetroActionStatus.BoardClosed);
        (await _sut.CastVoteAsync(Code, Facilitator, RetroVoteTarget.Card, cardId))
            .Status.Should().Be(RetroActionStatus.BoardClosed);
        (await _sut.AdvancePhaseAsync(Code, Facilitator))
            .Status.Should().Be(RetroActionStatus.BoardClosed);
        (await _sut.SetAnonymousAsync(Code, Facilitator, true))
            .Status.Should().Be(RetroActionStatus.BoardClosed);
        (await _sut.SetTemplateAsync(Code, Facilitator, RetroTemplate.FourLs, null))
            .Status.Should().Be(RetroActionStatus.BoardClosed);
    }

    [Fact]
    public async Task Actions_are_gone_once_the_board_is_deleted()
    {
        // Soft-delete hides the room from every read, carve-out or not.
        await SeedAsync();
        var added = await AddAsync();
        var actionId = added.Board!.Actions[0].Id;
        await _sut.DeleteBoardAsync(Code, Facilitator);

        var result = await _sut.ToggleActionDoneAsync(Code, Facilitator, actionId);

        result.Status.Should().Be(RetroActionStatus.BoardNotFound);
    }
}
