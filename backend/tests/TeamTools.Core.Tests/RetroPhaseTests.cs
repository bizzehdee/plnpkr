using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// The facilitator-driven phase machine (#23): the transitions, the gates each phase imposes, and
/// hidden collection — which, like anonymity, is enforced in the snapshot projection rather than by
/// asking the client not to render what it was sent.
/// </summary>
public class RetroPhaseTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly RetroService _sut;

    public RetroPhaseTests()
    {
        var rooms = TestServices.Rooms(_store, new StubShortCodeGenerator(Code), _clock);
        _sut = new RetroService(_store, rooms, _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Facilitator = "alice";
    private const string Bob = "bob";

    private async Task<RetroBoardSnapshot> SeedAsync()
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
        return (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
    }

    private async Task<RetroBoardSnapshot> BoardAsync(string forUserId) =>
        (await _sut.GetByShortCodeAsync(Code, forUserId))!;

    private async Task AdvanceToAsync(RetroPhase phase)
    {
        while ((await BoardAsync(Facilitator)).Phase != phase)
        {
            var result = await _sut.AdvancePhaseAsync(Code, Facilitator);
            result.Status.Should().Be(RetroActionStatus.Ok, $"advancing towards {phase}");
        }
    }

    // --- The order ---------------------------------------------------------

    [Fact]
    public async Task A_new_board_starts_in_collect()
    {
        var board = await SeedAsync();

        board.Phase.Should().Be(RetroPhase.Collect);
        board.PreviousPhase.Should().BeNull("there is nothing before collecting");
        board.NextPhase.Should().Be(RetroPhase.Group);
    }

    [Fact]
    public async Task Advancing_walks_the_phases_in_order()
    {
        await SeedAsync();

        var seen = new List<RetroPhase> { RetroPhase.Collect };
        for (var i = 0; i < 5; i++)
        {
            seen.Add((await _sut.AdvancePhaseAsync(Code, Facilitator)).Board!.Phase);
        }

        seen.Should().Equal(
            RetroPhase.Collect, RetroPhase.Group, RetroPhase.Vote,
            RetroPhase.Discuss, RetroPhase.Actions, RetroPhase.Closed);
    }

    [Fact]
    public async Task Advancing_past_the_end_is_refused()
    {
        await SeedAsync();
        await AdvanceToAsync(RetroPhase.Closed);

        var result = await _sut.AdvancePhaseAsync(Code, Facilitator);

        result.Status.Should().Be(RetroActionStatus.IllegalPhaseTransition);
    }

    [Fact]
    public async Task A_facilitator_can_step_back_one_phase_after_a_misclick()
    {
        await SeedAsync();
        await AdvanceToAsync(RetroPhase.Vote);

        var result = await _sut.PreviousPhaseAsync(Code, Facilitator);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Phase.Should().Be(RetroPhase.Group);
    }

    [Fact]
    public async Task Stepping_back_from_the_start_is_refused()
    {
        await SeedAsync();

        var result = await _sut.PreviousPhaseAsync(Code, Facilitator);

        result.Status.Should().Be(RetroActionStatus.IllegalPhaseTransition);
    }

    [Fact]
    public async Task Jumping_more_than_one_phase_is_refused()
    {
        // Skipping Vote would leave the discussion unordered — the thing phases exist to prevent.
        await SeedAsync();

        var result = await _sut.SetPhaseAsync(Code, Facilitator, RetroPhase.Discuss);

        result.Status.Should().Be(RetroActionStatus.IllegalPhaseTransition);
        (await BoardAsync(Facilitator)).Phase.Should().Be(RetroPhase.Collect);
    }

    [Fact]
    public async Task Setting_the_phase_to_the_adjacent_one_is_allowed()
    {
        await SeedAsync();

        var result = await _sut.SetPhaseAsync(Code, Facilitator, RetroPhase.Group);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Phase.Should().Be(RetroPhase.Group);
    }

    [Fact]
    public async Task Setting_the_phase_to_the_current_one_is_refused_as_a_non_transition()
    {
        await SeedAsync();

        var result = await _sut.SetPhaseAsync(Code, Facilitator, RetroPhase.Collect);

        result.Status.Should().Be(RetroActionStatus.IllegalPhaseTransition);
    }

    [Fact]
    public async Task A_participant_cannot_move_the_phase()
    {
        await SeedAsync();

        var result = await _sut.AdvancePhaseAsync(Code, Bob);

        result.Status.Should().Be(RetroActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task Phases_cannot_be_moved_on_a_closed_board()
    {
        await SeedAsync();
        await _sut.CloseBoardAsync(Code, Facilitator);

        var result = await _sut.AdvancePhaseAsync(Code, Facilitator);

        result.Status.Should().Be(RetroActionStatus.BoardClosed);
    }

    // --- Hidden collection -------------------------------------------------

    [Fact]
    public async Task During_collect_a_participant_sees_only_their_own_cards()
    {
        // The retro equivalent of hidden voting: nobody anchors on what has already been written.
        var board = await SeedAsync();
        var column = board.Columns[0].Id;
        await _sut.AddCardAsync(Code, Bob, column, "Bob's thought");
        await _sut.AddCardAsync(Code, Facilitator, column, "Alice's thought");

        var asSeenByBob = (await BoardAsync(Bob)).Columns[0];

        asSeenByBob.Cards.Should().ContainSingle().Which.Text.Should().Be("Bob's thought");
    }

    [Fact]
    public async Task During_collect_others_cards_are_reported_only_as_a_count()
    {
        // Enough to know the team is writing, not enough to be influenced by what.
        var board = await SeedAsync();
        var column = board.Columns[0].Id;
        await _sut.AddCardAsync(Code, Bob, column, "Bob's thought");
        await _sut.AddCardAsync(Code, Facilitator, column, "Alice's first");
        await _sut.AddCardAsync(Code, Facilitator, column, "Alice's second");

        var asSeenByBob = (await BoardAsync(Bob)).Columns[0];

        asSeenByBob.Cards.Should().HaveCount(1);
        asSeenByBob.HiddenCardCount.Should().Be(2);
    }

    [Fact]
    public async Task Ending_collection_reveals_everyones_cards()
    {
        var board = await SeedAsync();
        var column = board.Columns[0].Id;
        await _sut.AddCardAsync(Code, Bob, column, "Bob's thought");
        await _sut.AddCardAsync(Code, Facilitator, column, "Alice's thought");

        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var asSeenByBob = (await BoardAsync(Bob)).Columns[0];
        asSeenByBob.Cards.Select(c => c.Text).Should().Equal("Bob's thought", "Alice's thought");
        asSeenByBob.HiddenCardCount.Should().Be(0);
    }

    // --- Phase gates -------------------------------------------------------

    [Fact]
    public async Task Cards_cannot_be_added_after_collection_ends()
    {
        // A late card would invalidate the grouping and tallies built on the ones already there.
        var board = await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var result = await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "one more thing");

        result.Status.Should().Be(RetroActionStatus.WrongPhase);
    }

    [Fact]
    public async Task Cards_cannot_be_edited_after_collection_ends()
    {
        var board = await SeedAsync();
        var added = await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "as written");
        var cardId = added.Board!.Columns[0].Cards[0].Id;
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var result = await _sut.EditCardAsync(Code, Bob, cardId, "reworded later");

        result.Status.Should().Be(RetroActionStatus.WrongPhase);
    }

    [Fact]
    public async Task A_card_can_still_be_moved_and_deleted_after_collection_ends()
    {
        // Moving and deleting are how a facilitator tidies the board during Group; only the *text*
        // is frozen.
        var board = await SeedAsync();
        var added = await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "to tidy");
        var cardId = added.Board!.Columns[0].Cards[0].Id;
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var moved = await _sut.MoveCardAsync(Code, Bob, cardId, board.Columns[1].Id, 0);
        moved.Status.Should().Be(RetroActionStatus.Ok);

        var deleted = await _sut.DeleteCardAsync(Code, Bob, cardId);
        deleted.Status.Should().Be(RetroActionStatus.Ok);
    }

    // --- The countdown -----------------------------------------------------

    [Fact]
    public async Task Advancing_with_a_duration_starts_a_countdown_clients_can_tick_against()
    {
        await SeedAsync();

        var result = await _sut.AdvancePhaseAsync(Code, Facilitator, seconds: 300);

        result.Board!.PhaseDurationSeconds.Should().Be(300);
        result.Board.PhaseDeadline.Should().Be(_clock.UtcNow.AddSeconds(300));
    }

    [Fact]
    public async Task A_configured_duration_is_reused_by_the_next_phase()
    {
        await SeedAsync();
        await _sut.SetPhaseDurationAsync(Code, Facilitator, 120);

        var result = await _sut.AdvancePhaseAsync(Code, Facilitator);

        result.Board!.PhaseDeadline.Should().Be(_clock.UtcNow.AddSeconds(120));
    }

    [Fact]
    public async Task A_requested_duration_is_clamped_into_the_allowed_range()
    {
        await SeedAsync();

        var result = await _sut.AdvancePhaseAsync(Code, Facilitator, seconds: 1);

        result.Board!.PhaseDurationSeconds.Should().Be(RetroPhaseRules.MinPhaseSeconds);
    }

    [Fact]
    public async Task Moving_phase_never_inherits_the_previous_phases_running_countdown()
    {
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator, seconds: 300);
        await _sut.SetPhaseDurationAsync(Code, Facilitator, null);

        var result = await _sut.AdvancePhaseAsync(Code, Facilitator);

        result.Board!.PhaseDeadline.Should().BeNull();
    }

    [Fact]
    public async Task Closing_the_retro_runs_no_countdown()
    {
        await SeedAsync();
        await _sut.SetPhaseDurationAsync(Code, Facilitator, 120);
        await AdvanceToAsync(RetroPhase.Closed);

        (await BoardAsync(Facilitator)).PhaseDeadline.Should().BeNull();
    }

    [Fact]
    public async Task An_elapsed_countdown_is_cleared_without_moving_the_phase()
    {
        // A retro is facilitated: time running out is a prompt for the person running it, not a
        // reason to move a room full of people on mid-sentence.
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator, seconds: 60);
        _clock.Advance(TimeSpan.FromSeconds(61));

        var expired = await new RetroPhaseTimerService(_store, _clock).ExpireDuePhaseTimersAsync();

        expired.Should().ContainSingle().Which.ShortCode.Should().Be(Code);
        var board = await BoardAsync(Facilitator);
        board.PhaseDeadline.Should().BeNull();
        board.Phase.Should().Be(RetroPhase.Group, "the facilitator decides when to move on");
    }

    [Fact]
    public async Task A_countdown_that_has_not_elapsed_is_left_alone()
    {
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator, seconds: 300);
        _clock.Advance(TimeSpan.FromSeconds(30));

        var expired = await new RetroPhaseTimerService(_store, _clock).ExpireDuePhaseTimersAsync();

        expired.Should().BeEmpty();
        (await BoardAsync(Facilitator)).PhaseDeadline.Should().NotBeNull();
    }

    [Fact]
    public async Task The_expiry_sweep_ignores_boards_with_no_countdown()
    {
        await SeedAsync();

        var expired = await new RetroPhaseTimerService(_store, _clock).ExpireDuePhaseTimersAsync();

        expired.Should().BeEmpty();
    }
}
