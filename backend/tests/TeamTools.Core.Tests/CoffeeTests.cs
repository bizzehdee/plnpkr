using FluentAssertions;
using TeamTools.Core.Coffee;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// Lean Coffee (#35) — the third tool, and the test of whether the room engine earned its keep. The
/// phase rail, the dot budget, the countdown and the action-item rules are all shared primitives, so
/// what is actually asserted as *new* here is the per-topic timebox and the extension vote.
/// </summary>
public class CoffeeTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly CoffeeService _sut;

    public CoffeeTests()
    {
        _sut = TestServices.Coffee(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Facilitator = "alice";
    private const string Bob = "bob";

    private async Task SeedAsync(int? timebox = null)
    {
        await _sut.CreateAsync(new CreateCoffeeRequest(
            "Monday coffee", Facilitator, "Alice", Organise: true, TimeboxSeconds: timebox));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
    }

    private Task<CoffeeBoardSnapshot> BoardAsync(string forUserId = Facilitator) =>
        _sut.GetByShortCodeAsync(Code, forUserId).ContinueWith(t => t.Result!);

    /// <summary>Seeds two topics and walks the room to Discuss with Bob's topic ranked first.</summary>
    private async Task<(Guid Bobs, Guid Alices)> SeedToDiscussAsync(int? timebox = null)
    {
        await SeedAsync(timebox);
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        await _sut.AddTopicAsync(Code, Facilitator, "Standup length");

        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Vote
        var board = await BoardAsync();
        var bobs = board.Topics.First(t => t.Text == "Flaky CI").Id;
        var alices = board.Topics.First(t => t.Text == "Standup length").Id;

        await _sut.CastVoteAsync(Code, Bob, bobs);
        await _sut.CastVoteAsync(Code, Facilitator, bobs);
        await _sut.CastVoteAsync(Code, Facilitator, alices);

        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Discuss
        return (bobs, alices);
    }

    // --- Creation and the rail ---------------------------------------------

    [Fact]
    public async Task A_new_coffee_starts_in_propose_with_the_default_timebox()
    {
        await SeedAsync();

        var board = await BoardAsync();

        board.Phase.Should().Be(CoffeePhase.Propose);
        board.NextPhase.Should().Be(CoffeePhase.Vote);
        board.PreviousPhase.Should().BeNull("Propose is the start of the rail");
        board.PhaseDurationSeconds.Should().Be(CoffeePhaseRules.DefaultTimeboxSeconds);
    }

    [Fact]
    public async Task The_rail_runs_propose_vote_discuss_done()
    {
        await SeedAsync();

        var phases = new List<CoffeePhase>();
        for (var i = 0; i < 3; i++)
        {
            await _sut.AdvancePhaseAsync(Code, Facilitator);
            phases.Add((await BoardAsync()).Phase);
        }

        phases.Should().Equal(CoffeePhase.Vote, CoffeePhase.Discuss, CoffeePhase.Done);
    }

    [Fact]
    public async Task A_phase_jump_is_refused()
    {
        // The shared rail's rule (#35): one step at a time, so the room cannot land in a discussion
        // it never ranked.
        await SeedAsync();

        var result = await _sut.SetPhaseAsync(Code, Facilitator, CoffeePhase.Done);

        result.Status.Should().Be(CoffeeActionStatus.IllegalPhaseTransition);
    }

    [Fact]
    public async Task Only_an_organiser_moves_the_room_on()
    {
        await SeedAsync();

        var result = await _sut.AdvancePhaseAsync(Code, Bob);

        result.Status.Should().Be(CoffeeActionStatus.NotOrganiser);
    }

    // --- Topics -------------------------------------------------------------

    [Fact]
    public async Task Anyone_can_propose_a_topic_during_propose()
    {
        await SeedAsync();

        var result = await _sut.AddTopicAsync(Code, Bob, "Flaky CI");

        result.Status.Should().Be(CoffeeActionStatus.Ok);
        result.Board!.Topics.Should().ContainSingle().Which.Text.Should().Be("Flaky CI");
    }

    [Fact]
    public async Task A_topic_carries_its_author_because_it_is_volunteered()
    {
        // Deliberately unlike a retro card: proposing a topic means offering to talk about it, so
        // the name is the useful part rather than a leak. There is no anonymity flag on this board.
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var topic = (await BoardAsync()).Topics.Single();

        topic.AuthorUserId.Should().Be(Bob);
        topic.AuthorDisplayName.Should().Be("Bob");
    }

    [Fact]
    public async Task Topics_cannot_be_proposed_once_voting_has_started()
    {
        // A new topic after the dots are down would make the ranking a lie.
        await SeedAsync();
        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var result = await _sut.AddTopicAsync(Code, Bob, "Too late");

        result.Status.Should().Be(CoffeeActionStatus.WrongPhase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_topic_is_refused(string text)
    {
        await SeedAsync();

        (await _sut.AddTopicAsync(Code, Bob, text)).Status
            .Should().Be(CoffeeActionStatus.InvalidTopicText);
    }

    [Fact]
    public async Task An_over_long_topic_is_refused()
    {
        await SeedAsync();

        var result = await _sut.AddTopicAsync(Code, Bob, new string('x', CoffeeService.MaxTopicLength + 1));

        result.Status.Should().Be(CoffeeActionStatus.InvalidTopicText);
    }

    [Fact]
    public async Task Only_the_author_or_an_organiser_may_reword_a_topic()
    {
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        var topicId = (await BoardAsync(Bob)).Topics.Single().Id;
        await _sut.JoinAsync(new JoinSessionRequest(Code, "carol", "Carol", ParticipantRole.Voter));

        var stranger = await _sut.EditTopicAsync(Code, "carol", topicId, "Hijacked");
        var author = await _sut.EditTopicAsync(Code, Bob, topicId, "Flaky CI, still");
        var organiser = await _sut.EditTopicAsync(Code, Facilitator, topicId, "Moderated");

        stranger.Status.Should().Be(CoffeeActionStatus.NotTopicAuthor);
        author.Status.Should().Be(CoffeeActionStatus.Ok);
        organiser.Status.Should().Be(CoffeeActionStatus.Ok, "organisers moderate");
    }

    [Fact]
    public async Task Withdrawing_a_topic_takes_its_dots_with_it()
    {
        // Otherwise a voter's budget would stay spent on something nobody can vote for.
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        var topicId = (await BoardAsync(Bob)).Topics.Single().Id;
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        await _sut.CastVoteAsync(Code, Bob, topicId);
        await _sut.PreviousPhaseAsync(Code, Facilitator); // back to Propose to withdraw

        await _sut.DeleteTopicAsync(Code, Bob, topicId);

        var board = await BoardAsync(Bob);
        board.Topics.Should().BeEmpty();
        board.MyDotsRemaining.Should().Be(board.VoteBudget, "the dot came back");
    }

    // --- Hidden collection --------------------------------------------------

    [Fact]
    public async Task During_propose_a_participant_sees_only_their_own_topics()
    {
        // The same anti-anchoring rule as the retro's hidden collection (#23), enforced in the same
        // place: the per-recipient projection.
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        await _sut.AddTopicAsync(Code, Facilitator, "Standup length");

        var bobsView = await BoardAsync(Bob);

        bobsView.Topics.Should().ContainSingle().Which.Text.Should().Be("Flaky CI");
        bobsView.HiddenTopicCount.Should().Be(1, "he is told one exists, not what it says");
    }

    [Fact]
    public async Task Everyone_sees_every_topic_once_voting_opens()
    {
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        await _sut.AddTopicAsync(Code, Facilitator, "Standup length");

        await _sut.AdvancePhaseAsync(Code, Facilitator);

        var bobsView = await BoardAsync(Bob);
        bobsView.Topics.Should().HaveCount(2);
        bobsView.HiddenTopicCount.Should().Be(0);
    }

    // --- Dot voting (the shared budget) ------------------------------------

    [Fact]
    public async Task Dots_are_spent_and_the_allowance_comes_from_the_stored_rows()
    {
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        var topicId = (await BoardAsync(Bob)).Topics.Single().Id;

        var result = await _sut.CastVoteAsync(Code, Bob, topicId);

        result.Status.Should().Be(CoffeeActionStatus.Ok);
        result.Board!.MyDotsRemaining.Should().Be(CoffeeBoard.DefaultVoteBudget - 1);
    }

    [Fact]
    public async Task A_voter_out_of_dots_is_refused()
    {
        await SeedAsync();
        for (var i = 0; i < CoffeeBoard.DefaultVoteBudget + 1; i++)
        {
            await _sut.AddTopicAsync(Code, Bob, $"Topic {i}");
        }
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        var topics = (await BoardAsync(Bob)).Topics.Select(t => t.Id).ToList();

        for (var i = 0; i < CoffeeBoard.DefaultVoteBudget; i++)
        {
            (await _sut.CastVoteAsync(Code, Bob, topics[i])).Status.Should().Be(CoffeeActionStatus.Ok);
        }
        var overspend = await _sut.CastVoteAsync(Code, Bob, topics[^1]);

        overspend.Status.Should().Be(CoffeeActionStatus.OutOfDots);
    }

    [Fact]
    public async Task Stacking_is_refused_by_default_and_allowed_when_the_room_says_so()
    {
        // Spreading dots surfaces more of what the room wants to talk about, which is the point.
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        var topicId = (await BoardAsync(Bob)).Topics.Single().Id;
        await _sut.CastVoteAsync(Code, Bob, topicId);

        var stacked = await _sut.CastVoteAsync(Code, Bob, topicId);
        await _sut.SetAllowMultiplePerItemAsync(Code, Facilitator, true);
        var allowed = await _sut.CastVoteAsync(Code, Bob, topicId);

        stacked.Status.Should().Be(CoffeeActionStatus.AlreadyVotedForItem);
        allowed.Status.Should().Be(CoffeeActionStatus.Ok);
    }

    [Fact]
    public async Task Dot_totals_are_withheld_while_voting_is_open()
    {
        // A running total tells people where to put their remaining dots (#25).
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        var topicId = (await BoardAsync(Bob)).Topics.Single().Id;
        await _sut.CastVoteAsync(Code, Bob, topicId);

        var voting = await BoardAsync(Facilitator);
        voting.VoteTotalsVisible.Should().BeFalse();
        voting.Topics.Single().TotalDots.Should().BeNull();
        voting.Agenda.Should().BeEmpty("a ranking is a total by another name");

        await _sut.AdvancePhaseAsync(Code, Facilitator);
        var discussing = await BoardAsync(Facilitator);
        discussing.VoteTotalsVisible.Should().BeTrue();
        discussing.Topics.Single().TotalDots.Should().Be(1);
    }

    [Fact]
    public async Task A_voter_always_sees_their_own_dots()
    {
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        var topicId = (await BoardAsync(Bob)).Topics.Single().Id;
        await _sut.CastVoteAsync(Code, Bob, topicId);

        (await BoardAsync(Bob)).Topics.Single().MyDots.Should().Be(1);
        (await BoardAsync(Facilitator)).Topics.Single().MyDots.Should().Be(0);
    }

    [Fact]
    public async Task A_dot_can_be_taken_back()
    {
        await SeedAsync();
        await _sut.AddTopicAsync(Code, Bob, "Flaky CI");
        await _sut.AdvancePhaseAsync(Code, Facilitator);
        var topicId = (await BoardAsync(Bob)).Topics.Single().Id;
        await _sut.CastVoteAsync(Code, Bob, topicId);

        var withdrawn = await _sut.WithdrawVoteAsync(Code, Bob, topicId);
        var again = await _sut.WithdrawVoteAsync(Code, Bob, topicId);

        withdrawn.Board!.MyDotsRemaining.Should().Be(CoffeeBoard.DefaultVoteBudget);
        again.Status.Should().Be(CoffeeActionStatus.NoVoteToWithdraw);
    }

    // --- The agenda and the discussion -------------------------------------

    [Fact]
    public async Task The_agenda_is_ranked_by_dots_and_the_room_starts_on_the_top_topic()
    {
        var (bobs, _) = await SeedToDiscussAsync();

        var board = await BoardAsync();

        board.Agenda.Select(t => t.Text).Should().Equal("Flaky CI", "Standup length");
        board.CurrentTopicId.Should().Be(bobs, "the room starts on what it voted for most");
        board.PhaseDeadline.Should().NotBeNull("the timebox starts with the topic");
    }

    [Fact]
    public async Task Next_topic_closes_the_current_one_and_starts_the_following_one()
    {
        var (bobs, alices) = await SeedToDiscussAsync();

        await _sut.NextTopicAsync(Code, Facilitator);

        var board = await BoardAsync();
        board.CurrentTopicId.Should().Be(alices);
        board.Topics.First(t => t.Id == bobs).IsDiscussed.Should().BeTrue();
    }

    [Fact]
    public async Task Working_the_whole_list_leaves_no_current_topic()
    {
        // The signal for "you are done" — and the point at which the facilitator moves to Done.
        await SeedToDiscussAsync();

        await _sut.NextTopicAsync(Code, Facilitator);
        await _sut.NextTopicAsync(Code, Facilitator);

        var board = await BoardAsync();
        board.CurrentTopicId.Should().BeNull();
        board.PhaseDeadline.Should().BeNull();
        board.Topics.Should().OnlyContain(t => t.IsDiscussed);
    }

    [Fact]
    public async Task The_timebox_restarts_for_each_topic()
    {
        // The defining shape of a Lean Coffee: the countdown is per topic, not per phase.
        var (_, _) = await SeedToDiscussAsync(timebox: 120);
        var first = (await BoardAsync()).PhaseDeadline;

        _clock.Advance(TimeSpan.FromSeconds(60));
        await _sut.NextTopicAsync(Code, Facilitator);

        var second = (await BoardAsync()).PhaseDeadline;
        second.Should().BeAfter(first!.Value, "the next topic gets its own full timebox");
    }

    [Fact]
    public async Task Time_actually_spent_is_recorded_against_the_topic()
    {
        // The interesting number in the minutes: what the room said it wanted versus what it did.
        var (bobs, _) = await SeedToDiscussAsync(timebox: 300);

        _clock.Advance(TimeSpan.FromSeconds(90));
        await _sut.NextTopicAsync(Code, Facilitator);

        var topic = (await BoardAsync()).Topics.First(t => t.Id == bobs);
        topic.DiscussedSeconds.Should().BeCloseTo(90, 2);
    }

    // --- The extension vote (the genuinely new part) ------------------------

    [Fact]
    public async Task An_expired_timebox_opens_the_extension_vote_rather_than_moving_on()
    {
        // Poker auto-reveals because a reveal is mechanical; a retro does nothing at all; this asks
        // the room. The difference between the three is the product, not an inconsistency.
        var (bobs, _) = await SeedToDiscussAsync(timebox: 60);
        _clock.Advance(TimeSpan.FromSeconds(61));

        var expired = await TestServices
            .CoffeeTimers(_store, new StubShortCodeGenerator(Code), _clock)
            .ExpireDueTimeboxesAsync();

        expired.Should().ContainSingle();
        var board = await BoardAsync();
        board.CurrentTopicId.Should().Be(bobs, "still on the same topic");
        board.ExtendVote.Should().NotBeNull();
        board.ExtendVote!.TopicId.Should().Be(bobs);
    }

    [Fact]
    public async Task Extension_answers_are_hidden_until_the_facilitator_resolves_the_vote()
    {
        // Otherwise the room follows whoever clicked first.
        await OpenExtensionVoteAsync();

        await _sut.VoteOnExtensionAsync(Code, Bob, ExtendChoice.KeepGoing);

        var alicesView = await BoardAsync(Facilitator);
        alicesView.ExtendVote!.Answered.Should().Be(1, "the count is public");
        alicesView.ExtendVote.KeepGoing.Should().BeNull("the split is not, yet");
        alicesView.ExtendVote.MoveOn.Should().BeNull();
        alicesView.ExtendVote.MyChoice.Should().BeNull("Alice has not voted");

        var bobsView = await BoardAsync(Bob);
        bobsView.ExtendVote!.MyChoice.Should().Be(ExtendChoice.KeepGoing, "his own is his to see");
    }

    [Fact]
    public async Task A_majority_to_keep_going_restarts_the_timebox_and_counts_an_extension()
    {
        var (bobs, _) = await OpenExtensionVoteAsync();
        await _sut.VoteOnExtensionAsync(Code, Bob, ExtendChoice.KeepGoing);
        await _sut.VoteOnExtensionAsync(Code, Facilitator, ExtendChoice.KeepGoing);

        await _sut.ResolveExtensionAsync(Code, Facilitator);

        var board = await BoardAsync();
        board.CurrentTopicId.Should().Be(bobs);
        board.PhaseDeadline.Should().NotBeNull("the clock is running again");
        board.Topics.First(t => t.Id == bobs).Extensions.Should().Be(1);
        board.ExtendVote.Should().BeNull("the vote is over and cleared");
    }

    [Fact]
    public async Task A_majority_to_move_on_finishes_the_topic()
    {
        var (bobs, alices) = await OpenExtensionVoteAsync();
        await _sut.VoteOnExtensionAsync(Code, Bob, ExtendChoice.MoveOn);
        await _sut.VoteOnExtensionAsync(Code, Facilitator, ExtendChoice.MoveOn);

        await _sut.ResolveExtensionAsync(Code, Facilitator);

        var board = await BoardAsync();
        board.CurrentTopicId.Should().Be(alices);
        board.Topics.First(t => t.Id == bobs).IsDiscussed.Should().BeTrue();
    }

    [Fact]
    public async Task A_tie_moves_the_room_on()
    {
        // Keep-going has to *win*, not merely draw: the default is to respect the timebox the room
        // agreed to.
        var (bobs, _) = await OpenExtensionVoteAsync();
        await _sut.VoteOnExtensionAsync(Code, Bob, ExtendChoice.KeepGoing);
        await _sut.VoteOnExtensionAsync(Code, Facilitator, ExtendChoice.MoveOn);

        await _sut.ResolveExtensionAsync(Code, Facilitator);

        (await BoardAsync()).Topics.First(t => t.Id == bobs).IsDiscussed.Should().BeTrue();
    }

    [Fact]
    public async Task A_voter_may_change_their_mind_before_the_reveal()
    {
        await OpenExtensionVoteAsync();

        await _sut.VoteOnExtensionAsync(Code, Bob, ExtendChoice.KeepGoing);
        await _sut.VoteOnExtensionAsync(Code, Bob, ExtendChoice.MoveOn);

        var board = await BoardAsync(Bob);
        board.ExtendVote!.Answered.Should().Be(1, "changing a mind is not a second vote");
        board.ExtendVote.MyChoice.Should().Be(ExtendChoice.MoveOn);
    }

    [Fact]
    public async Task Voting_on_an_extension_that_is_not_running_is_refused()
    {
        await SeedToDiscussAsync();

        var result = await _sut.VoteOnExtensionAsync(Code, Bob, ExtendChoice.KeepGoing);

        result.Status.Should().Be(CoffeeActionStatus.NoExtendVoteRunning);
    }

    [Fact]
    public async Task Only_an_organiser_resolves_the_extension_vote()
    {
        // "The vote was 3-2, let's give it two more minutes" is a judgement a person makes with the
        // room in front of them.
        await OpenExtensionVoteAsync();

        (await _sut.ResolveExtensionAsync(Code, Bob)).Status
            .Should().Be(CoffeeActionStatus.NotOrganiser);
    }

    private async Task<(Guid Bobs, Guid Alices)> OpenExtensionVoteAsync()
    {
        var ids = await SeedToDiscussAsync(timebox: 60);
        _clock.Advance(TimeSpan.FromSeconds(61));
        await TestServices
            .CoffeeTimers(_store, new StubShortCodeGenerator(Code), _clock)
            .ExpireDueTimeboxesAsync();
        return ids;
    }

    // --- Decisions ----------------------------------------------------------

    [Fact]
    public async Task A_decision_can_be_recorded_against_the_current_topic()
    {
        var (bobs, _) = await SeedToDiscussAsync();

        var result = await _sut.AddDecisionAsync(
            Code, Facilitator, "Quarantine the flaky test", bobs, Bob, null, null);

        result.Status.Should().Be(CoffeeActionStatus.Ok);
        var decision = result.Board!.Decisions.Should().ContainSingle().Subject;
        decision.Title.Should().Be("Quarantine the flaky test");
        decision.TopicId.Should().Be(bobs);
        decision.OwnerName.Should().Be("Bob", "resolved from the participant, so it stays current");
    }

    [Fact]
    public async Task A_decision_owner_may_be_someone_who_was_never_in_the_room()
    {
        // The platform has no accounts, and the person who ends up owning something may not have
        // been there — the shared ActionItemRules answer (#26/#35).
        var (_, _) = await SeedToDiscussAsync();

        var result = await _sut.AddDecisionAsync(
            Code, Facilitator, "Ask the platform team", null, null, "Dana", null);

        result.Board!.Decisions.Single().OwnerName.Should().Be("Dana");
    }

    [Fact]
    public async Task Decisions_cannot_be_recorded_before_the_discussion()
    {
        await SeedAsync();

        var result = await _sut.AddDecisionAsync(Code, Facilitator, "Too early", null, null, null, null);

        result.Status.Should().Be(CoffeeActionStatus.WrongPhase);
    }

    [Fact]
    public async Task A_blank_decision_title_is_refused()
    {
        await SeedToDiscussAsync();

        (await _sut.AddDecisionAsync(Code, Facilitator, "   ", null, null, null, null)).Status
            .Should().Be(CoffeeActionStatus.InvalidDecisionTitle);
    }

    [Fact]
    public async Task A_decision_stays_editable_after_the_room_closes()
    {
        // The same carve-out as the retro's action items (#26): "we did that" gets ticked days later.
        await SeedToDiscussAsync();
        await _sut.AddDecisionAsync(Code, Facilitator, "Quarantine the flaky test", null, null, null, null);
        var decisionId = (await BoardAsync()).Decisions.Single().Id;
        await _sut.CloseBoardAsync(Code, Facilitator);

        var ticked = await _sut.ToggleDecisionDoneAsync(Code, Facilitator, decisionId);

        ticked.Status.Should().Be(CoffeeActionStatus.Ok);
        ticked.Board!.Decisions.Single().IsDone.Should().BeTrue();
    }

    [Fact]
    public async Task Outstanding_decisions_come_before_the_ones_already_done()
    {
        await SeedToDiscussAsync();
        await _sut.AddDecisionAsync(Code, Facilitator, "First", null, null, null, null);
        await _sut.AddDecisionAsync(Code, Facilitator, "Second", null, null, null, null);
        var first = (await BoardAsync()).Decisions.First(d => d.Title == "First").Id;

        await _sut.ToggleDecisionDoneAsync(Code, Facilitator, first);

        (await BoardAsync()).Decisions.Select(d => d.Title).Should().Equal("Second", "First");
    }

    // --- One tool per room --------------------------------------------------

    [Fact]
    public async Task Joining_a_coffee_room_over_another_tool_is_refused()
    {
        // The room engine's rule (#32), inherited rather than restated.
        var poker = TestServices.Poker(_store, new StubShortCodeGenerator(Code), _clock);
        await poker.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, Facilitator, "Alice", Organise: true));

        var result = await _sut.JoinAsync(
            new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.WrongTool);
    }
}
