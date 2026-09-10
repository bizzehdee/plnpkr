using FluentAssertions;
using TeamTools.Core.Coffee;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Security;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// The room-level half of Lean Coffee (#35): the operations the tool inherits from the room engine
/// and only projects — leave, presence, roles, organisers, password, close, delete.
/// <para>
/// These are thin by design, and that is exactly why they are worth a pass: each one is a place
/// where a tool can forget to project, or project the wrong viewer's board. The retro has the same
/// suite for the same reason (#21).
/// </para>
/// </summary>
public class CoffeeRoomOperationsTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly CoffeeService _sut;

    public CoffeeRoomOperationsTests()
    {
        _sut = TestServices.Coffee(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Facilitator = "alice";
    private const string Bob = "bob";

    private async Task SeedAsync(string? password = null)
    {
        await _sut.CreateAsync(new CreateCoffeeRequest(
            "Monday coffee", Facilitator, "Alice", Organise: true, Password: password));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter, password));
    }

    private Task<CoffeeBoardSnapshot> BoardAsync(string forUserId = Facilitator) =>
        _sut.GetByShortCodeAsync(Code, forUserId).ContinueWith(t => t.Result!);

    // --- Creation edge cases ------------------------------------------------

    [Theory]
    [InlineData("", "Alice")]
    [InlineData("   ", "Alice")]
    [InlineData("Monday coffee", "")]
    [InlineData("Monday coffee", "  ")]
    public async Task Creation_needs_a_name_and_a_display_name(string name, string displayName)
    {
        var result = await _sut.CreateAsync(new CreateCoffeeRequest(
            name, Facilitator, displayName, Organise: true));

        result.Status.Should().Be(CreateCoffeeStatus.InvalidName);
        result.Board.Should().BeNull();
    }

    [Fact]
    public async Task A_requested_timebox_is_clamped_into_range()
    {
        // The shared Countdown primitive (#34); the bounds are this tool's.
        await _sut.CreateAsync(new CreateCoffeeRequest(
            "Fast round", Facilitator, "Alice", Organise: true, TimeboxSeconds: 1));

        (await BoardAsync()).PhaseDurationSeconds
            .Should().Be(CoffeePhaseRules.MinTimeboxSeconds);
    }

    // --- Membership and presence -------------------------------------------

    [Fact]
    public async Task Leaving_removes_the_seat_and_projects_the_board_back()
    {
        await SeedAsync();

        var result = await _sut.LeaveAsync(Code, Bob);

        result.Status.Should().Be(CoffeeActionStatus.Ok);
        result.Board!.Room.Participants.Should().OnlyContain(p => p.UserId == Facilitator);
    }

    [Fact]
    public async Task Leaving_a_room_that_is_not_there_is_reported_as_not_found()
    {
        (await _sut.LeaveAsync("no-such-room", Bob)).Status
            .Should().Be(CoffeeActionStatus.BoardNotFound);
    }

    [Fact]
    public async Task A_dropped_connection_marks_the_participant_away_rather_than_removing_them()
    {
        await SeedAsync();

        var result = await _sut.MarkDisconnectedAsync(Code, Bob);

        result.Status.Should().Be(CoffeeActionStatus.Ok);
        var bob = result.Board!.Room.Participants.Single(p => p.UserId == Bob);
        bob.IsConnected.Should().BeFalse("the seat is kept — idle eviction cleans up if he stays away");
    }

    [Fact]
    public async Task A_participant_can_switch_between_voter_and_observer()
    {
        await SeedAsync();

        var result = await _sut.ChangeRoleAsync(Code, Bob, Bob, ParticipantRole.Observer);

        result.Status.Should().Be(CoffeeActionStatus.Ok);
        result.Board!.Room.Participants.Single(p => p.UserId == Bob)
            .Role.Should().Be(ParticipantRole.Observer);
    }

    [Fact]
    public async Task An_organiser_can_forbid_participants_changing_their_own_role()
    {
        await SeedAsync();

        await _sut.SetAllowRoleChangeAsync(Code, Facilitator, false);
        var refused = await _sut.ChangeRoleAsync(Code, Bob, Bob, ParticipantRole.Observer);

        refused.Status.Should().NotBe(CoffeeActionStatus.Ok);
        (await BoardAsync()).Room.AllowRoleChange.Should().BeFalse();
    }

    [Fact]
    public async Task Reactions_can_be_turned_off_for_the_room()
    {
        await SeedAsync();

        await _sut.SetReactionsEnabledAsync(Code, Facilitator, false);

        (await _sut.AreReactionsEnabledAsync(Code)).Should().BeFalse();
        (await BoardAsync()).Room.ReactionsEnabled.Should().BeFalse();
    }

    // --- Organisers ---------------------------------------------------------

    [Fact]
    public async Task An_organiser_can_promote_demote_and_transfer()
    {
        await SeedAsync();

        var promoted = await _sut.PromoteToOrganiserAsync(Code, Facilitator, Bob);
        promoted.Board!.Room.Participants.Single(p => p.UserId == Bob)
            .IsOrganiser.Should().BeTrue();

        var demoted = await _sut.DemoteOrganiserAsync(Code, Facilitator, Bob);
        demoted.Board!.Room.Participants.Single(p => p.UserId == Bob)
            .IsOrganiser.Should().BeFalse();

        var transferred = await _sut.TransferOrganiserAsync(Code, Facilitator, Bob);
        transferred.Board!.Room.Participants.Single(p => p.UserId == Bob)
            .IsOrganiser.Should().BeTrue();
        transferred.Board.Room.Participants.Single(p => p.UserId == Facilitator)
            .IsOrganiser.Should().BeFalse("a transfer hands it over rather than sharing it");
    }

    [Fact]
    public async Task A_participant_cannot_promote_themselves()
    {
        await SeedAsync();

        (await _sut.PromoteToOrganiserAsync(Code, Bob, Bob)).Status
            .Should().Be(CoffeeActionStatus.NotOrganiser);
    }

    // --- Password and lifecycle --------------------------------------------

    [Fact]
    public async Task An_organiser_can_set_and_clear_the_room_password()
    {
        var sut = TestServices.Coffee(
            _store, new StubShortCodeGenerator(Code), _clock, new Pbkdf2PasswordHasher());
        await sut.CreateAsync(new CreateCoffeeRequest(
            "Monday coffee", Facilitator, "Alice", Organise: true));

        await sut.SetPasswordAsync(Code, Facilitator, "hunter2");
        (await sut.GetByShortCodeAsync(Code, Facilitator))!.Room.HasPassword.Should().BeTrue();

        await sut.SetPasswordAsync(Code, Facilitator, null);
        (await sut.GetByShortCodeAsync(Code, Facilitator))!.Room.HasPassword.Should().BeFalse();
    }

    [Fact]
    public async Task Closing_the_room_freezes_it_but_leaves_it_readable()
    {
        await SeedAsync();

        var result = await _sut.CloseBoardAsync(Code, Facilitator);

        result.Status.Should().Be(CoffeeActionStatus.Ok);
        result.Board!.Room.IsClosed.Should().BeTrue();
        (await _sut.AddTopicAsync(Code, Bob, "Too late")).Status
            .Should().NotBe(CoffeeActionStatus.Ok, "a closed room is read-only");
    }

    [Fact]
    public async Task Deleting_the_room_leaves_nothing_to_project()
    {
        await SeedAsync();

        var result = await _sut.DeleteBoardAsync(Code, Facilitator);

        result.Status.Should().Be(CoffeeActionStatus.Ok);
        result.Board.Should().BeNull("there is no board left to send");
        (await _sut.GetByShortCodeAsync(Code, Facilitator)).Should().BeNull();
    }

    [Fact]
    public async Task Only_an_organiser_closes_or_deletes()
    {
        await SeedAsync();

        (await _sut.CloseBoardAsync(Code, Bob)).Status.Should().Be(CoffeeActionStatus.NotOrganiser);
        (await _sut.DeleteBoardAsync(Code, Bob)).Status.Should().Be(CoffeeActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task Reading_a_room_that_is_not_a_coffee_room_gives_nothing()
    {
        var poker = TestServices.Poker(_store, new StubShortCodeGenerator(Code), _clock);
        await poker.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, Facilitator, "Alice", Organise: true));

        (await _sut.GetByShortCodeAsync(Code, Facilitator)).Should().BeNull();
    }

    [Fact]
    public async Task A_non_participant_cannot_write_to_the_board()
    {
        await SeedAsync();

        (await _sut.AddTopicAsync(Code, "stranger", "Let me in")).Status
            .Should().Be(CoffeeActionStatus.NotParticipant);
    }

    // --- Settings and decision CRUD ----------------------------------------

    [Fact]
    public async Task The_dot_budget_can_be_changed_within_bounds()
    {
        await SeedAsync();

        var ok = await _sut.SetVoteBudgetAsync(Code, Facilitator, 5);
        var tooLow = await _sut.SetVoteBudgetAsync(Code, Facilitator, CoffeeService.MinVoteBudget - 1);
        var tooHigh = await _sut.SetVoteBudgetAsync(Code, Facilitator, CoffeeService.MaxVoteBudget + 1);

        ok.Board!.VoteBudget.Should().Be(5);
        tooLow.Status.Should().Be(CoffeeActionStatus.InvalidVoteBudget);
        tooHigh.Status.Should().Be(CoffeeActionStatus.InvalidVoteBudget);
    }

    [Fact]
    public async Task Only_an_organiser_changes_the_budget_or_the_timebox()
    {
        await SeedAsync();

        (await _sut.SetVoteBudgetAsync(Code, Bob, 5)).Status
            .Should().Be(CoffeeActionStatus.NotOrganiser);
        (await _sut.SetTimeboxAsync(Code, Bob, 120)).Status
            .Should().Be(CoffeeActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task The_timebox_can_be_changed_and_cleared()
    {
        await SeedAsync();

        (await _sut.SetTimeboxAsync(Code, Facilitator, 120)).Board!
            .PhaseDurationSeconds.Should().Be(120);
        (await _sut.SetTimeboxAsync(Code, Facilitator, null)).Board!
            .PhaseDurationSeconds.Should().BeNull("null means no countdown at all");
    }

    [Fact]
    public async Task A_decision_can_be_edited_and_removed()
    {
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Vote
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Discuss
        await _sut.AddDecisionAsync(Code, Facilitator, "Quarantine it", null, null, null, null);
        var id = (await BoardAsync()).Decisions.Single().Id;

        var edited = await _sut.EditDecisionAsync(
            Code, Facilitator, id, "Quarantine and file a ticket", Bob, null,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        edited.Board!.Decisions.Single().Title.Should().Be("Quarantine and file a ticket");
        edited.Board.Decisions.Single().OwnerName.Should().Be("Bob");
        edited.Board.Decisions.Single().DueDate.Should().NotBeNull();

        var removed = await _sut.DeleteDecisionAsync(Code, Facilitator, id);
        removed.Board!.Decisions.Should().BeEmpty();
    }

    [Fact]
    public async Task Editing_a_decision_to_a_blank_title_is_refused()
    {
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.AddDecisionAsync(Code, Facilitator, "Quarantine it", null, null, null, null);
        var id = (await BoardAsync()).Decisions.Single().Id;

        (await _sut.EditDecisionAsync(Code, Facilitator, id, "  ", null, null, null)).Status
            .Should().Be(CoffeeActionStatus.InvalidDecisionTitle);
    }

    [Fact]
    public async Task Operating_on_a_decision_that_is_not_there_is_reported_as_not_found()
    {
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        var ghost = Guid.NewGuid();

        (await _sut.EditDecisionAsync(Code, Facilitator, ghost, "x", null, null, null)).Status
            .Should().Be(CoffeeActionStatus.DecisionNotFound);
        (await _sut.ToggleDecisionDoneAsync(Code, Facilitator, ghost)).Status
            .Should().Be(CoffeeActionStatus.DecisionNotFound);
        (await _sut.DeleteDecisionAsync(Code, Facilitator, ghost)).Status
            .Should().Be(CoffeeActionStatus.DecisionNotFound);
    }

    [Fact]
    public async Task A_decision_against_a_topic_that_does_not_exist_is_refused()
    {
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var result = await _sut.AddDecisionAsync(
            Code, Facilitator, "Against nothing", Guid.NewGuid(), null, null, null);

        result.Status.Should().Be(CoffeeActionStatus.TopicNotFound);
    }

    // --- Topic and phase edge cases ----------------------------------------

    [Fact]
    public async Task Operating_on_a_topic_that_is_not_there_is_reported_as_not_found()
    {
        await SeedAsync();
        var ghost = Guid.NewGuid();

        (await _sut.EditTopicAsync(Code, Bob, ghost, "x")).Status
            .Should().Be(CoffeeActionStatus.TopicNotFound);
        (await _sut.DeleteTopicAsync(Code, Bob, ghost)).Status
            .Should().Be(CoffeeActionStatus.TopicNotFound);
    }

    [Fact]
    public async Task Voting_for_a_topic_that_is_not_there_is_refused()
    {
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        (await _sut.CastVoteAsync(Code, Bob, Guid.NewGuid())).Status
            .Should().Be(CoffeeActionStatus.TopicNotFound);
    }

    [Fact]
    public async Task Dots_cannot_be_spent_or_withdrawn_outside_the_vote_phase()
    {
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        var topicId = (await BoardAsync(Bob)).Topics.Single().Id;

        (await _sut.CastVoteAsync(Code, Bob, topicId)).Status
            .Should().Be(CoffeeActionStatus.WrongPhase);
        (await _sut.WithdrawVoteAsync(Code, Bob, topicId)).Status
            .Should().Be(CoffeeActionStatus.WrongPhase);
    }

    [Fact]
    public async Task Rewording_or_withdrawing_a_topic_is_refused_once_voting_starts()
    {
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        var topicId = (await BoardAsync(Bob)).Topics.Single().Id;
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        (await _sut.EditTopicAsync(Code, Bob, topicId, "Reworded")).Status
            .Should().Be(CoffeeActionStatus.WrongPhase);
        (await _sut.DeleteTopicAsync(Code, Bob, topicId)).Status
            .Should().Be(CoffeeActionStatus.WrongPhase);
    }

    [Fact]
    public async Task An_over_long_reword_is_refused()
    {
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        var topicId = (await BoardAsync(Bob)).Topics.Single().Id;

        var result = await _sut.EditTopicAsync(
            Code, Bob, topicId, new string('x', CoffeeService.MaxTopicLength + 1));

        result.Status.Should().Be(CoffeeActionStatus.InvalidTopicText);
    }

    [Fact]
    public async Task Stepping_back_off_the_start_or_forward_off_the_end_is_refused()
    {
        await SeedAsync();

        (await _sut.PreviousPhaseAsync(Code, Facilitator)).Status
            .Should().Be(CoffeeActionStatus.IllegalPhaseTransition, "Propose is the first phase");

        for (var i = 0; i < 3; i++)
        {
            await _sut.AdvancePhaseAsync(Code, Facilitator);
        }

        (await _sut.AdvancePhaseAsync(Code, Facilitator)).Status
            .Should().Be(CoffeeActionStatus.IllegalPhaseTransition, "Done is the last");
    }

    [Fact]
    public async Task Leaving_discuss_stops_the_clock_and_clears_the_current_topic()
    {
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Vote
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Discuss
        (await BoardAsync()).CurrentTopicId.Should().NotBeNull();

        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Done

        var board = await BoardAsync();
        board.CurrentTopicId.Should().BeNull();
        board.PhaseDeadline.Should().BeNull();
    }

    [Fact]
    public async Task Next_topic_outside_the_discussion_is_refused()
    {
        await SeedAsync();

        (await _sut.NextTopicAsync(Code, Facilitator)).Status
            .Should().Be(CoffeeActionStatus.WrongPhase);
    }

    [Fact]
    public async Task Resolving_an_extension_vote_that_is_not_running_is_refused()
    {
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        (await _sut.ResolveExtensionAsync(Code, Facilitator)).Status
            .Should().Be(CoffeeActionStatus.NoExtendVoteRunning);
    }

    [Fact]
    public async Task Discuss_with_no_topics_has_nothing_to_start_on()
    {
        // A room that proposed nothing is a legitimate, if sad, outcome.
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var board = await BoardAsync();
        board.Phase.Should().Be(CoffeePhase.Discuss);
        board.CurrentTopicId.Should().BeNull();
        board.PhaseDeadline.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_room_is_reported_as_not_found_for_every_write()
    {
        var ghost = Guid.NewGuid();

        (await _sut.AddTopicAsync("no-such-room", Bob, "x")).Status
            .Should().Be(CoffeeActionStatus.BoardNotFound);
        (await _sut.EditTopicAsync("no-such-room", Bob, ghost, "x")).Status
            .Should().Be(CoffeeActionStatus.BoardNotFound);
        (await _sut.AdvancePhaseAsync("no-such-room", Facilitator)).Status
            .Should().Be(CoffeeActionStatus.BoardNotFound);
        (await _sut.AddDecisionAsync("no-such-room", Facilitator, "x", null, null, null, null)).Status
            .Should().Be(CoffeeActionStatus.BoardNotFound);
        (await _sut.SetPhaseAsync("no-such-room", Facilitator, CoffeePhase.Vote)).Status
            .Should().Be(CoffeeActionStatus.BoardNotFound);
    }
}
