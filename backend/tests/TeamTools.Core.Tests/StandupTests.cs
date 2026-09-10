using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Standup;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// Async Standup (#36) — the fourth tool, and the one whose spec was mostly about what the platform
/// will *not* do for it.
/// <para>
/// The rule that matters is <b>post-to-read</b>, and it is a property of the wire rather than of the
/// UI. Most of what follows exists to hold that, and to hold the two deliberate absences: no phase
/// rail, and no naming of who has not posted.
/// </para>
/// </summary>
public class StandupTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly StandupService _sut;

    public StandupTests()
    {
        _sut = TestServices.Standup(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Alice = "alice";
    private const string Bob = "bob";

    private async Task SeedAsync(IReadOnlyList<string>? questions = null, string? password = null)
    {
        await _sut.CreateAsync(new CreateStandupRequest(
            "Monday standup", Alice, "Alice", Organise: true, Password: password,
            Questions: questions));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter, password));
    }

    private async Task<StandupBoardSnapshot> BoardAsync(string forUserId = Alice) =>
        (await _sut.GetByShortCodeAsync(Code, forUserId))!;

    private async Task<Guid> FirstQuestionAsync() => (await BoardAsync()).Questions[0].Id;

    // --- Creation -----------------------------------------------------------

    [Fact]
    public async Task A_new_standup_asks_the_three_usual_questions()
    {
        await SeedAsync();

        var board = await BoardAsync();

        board.Questions.Should().HaveCount(3);
        board.Questions[0].Text.Should().Be(StandupQuestions.Default[0]);
    }

    [Fact]
    public async Task A_room_can_ask_its_own_questions()
    {
        await SeedAsync(questions: ["Wins?", "Worries?"]);

        (await BoardAsync()).Questions.Select(q => q.Text).Should().Equal("Wins?", "Worries?");
    }

    [Fact]
    public async Task Blank_questions_are_dropped_rather_than_asked()
    {
        await SeedAsync(questions: ["Wins?", "   ", ""]);

        (await BoardAsync()).Questions.Should().ContainSingle().Which.Text.Should().Be("Wins?");
    }

    [Fact]
    public async Task An_all_blank_question_set_falls_back_to_the_defaults()
    {
        // Rather than creating a standup that asks nothing, which nobody could answer.
        await SeedAsync(questions: ["  ", ""]);

        (await BoardAsync()).Questions.Should().HaveCount(3);
    }

    [Fact]
    public async Task Too_many_questions_are_refused()
    {
        var many = Enumerable.Range(1, StandupService.MaxQuestions + 1).Select(i => $"Q{i}").ToList();

        var result = await _sut.CreateAsync(new CreateStandupRequest(
            "Long standup", Alice, "Alice", Organise: true, Questions: many));

        result.Status.Should().Be(CreateStandupStatus.InvalidQuestions);
    }

    [Theory]
    [InlineData("", "Alice")]
    [InlineData("Monday standup", "  ")]
    public async Task Creation_needs_a_name_and_a_display_name(string name, string displayName)
    {
        var result = await _sut.CreateAsync(new CreateStandupRequest(
            name, Alice, displayName, Organise: true));

        result.Status.Should().Be(CreateStandupStatus.InvalidName);
    }

    [Fact]
    public async Task There_is_no_phase_rail_to_move_along()
    {
        // Deliberate (#36): a standup opens, people post, it closes. This is the tool that shows the
        // rail #35 extracted is a retro/coffee concern rather than a platform one — so the snapshot
        // has no phase on it at all.
        await SeedAsync();

        var board = await BoardAsync();

        board.Should().BeOfType<StandupBoardSnapshot>();
        typeof(StandupBoardSnapshot).GetProperty("Phase").Should().BeNull();
        typeof(StandupBoardSnapshot).GetProperty("NextPhase").Should().BeNull();
    }

    // --- Post-to-read -------------------------------------------------------

    [Fact]
    public async Task Before_posting_you_see_nobody_elses_answers()
    {
        // The anti-anchoring rule: a standup you read first is a standup you write to match.
        await SeedAsync();
        var q = await FirstQuestionAsync();
        await _sut.AnswerAsync(Code, Alice, q, "Shipped the export");

        var bobsView = await BoardAsync(Bob);

        bobsView.IHavePosted.Should().BeFalse();
        bobsView.People.Should().ContainSingle(
            "Bob reads only his own standup until he has posted")
            .Which.IsMe.Should().BeTrue();
    }

    [Fact]
    public async Task Posting_unlocks_everyone_elses_answers()
    {
        await SeedAsync();
        var q = await FirstQuestionAsync();
        await _sut.AnswerAsync(Code, Alice, q, "Shipped the export");

        await _sut.AnswerAsync(Code, Bob, q, "Fixed the flaky test");

        var bobsView = await BoardAsync(Bob);
        bobsView.IHavePosted.Should().BeTrue();
        bobsView.People.Should().HaveCount(2);
        bobsView.People.Single(p => p.UserId == Alice).Answers.Single().Text
            .Should().Be("Shipped the export");
    }

    [Fact]
    public async Task You_always_see_your_own_answers()
    {
        await SeedAsync();
        var q = await FirstQuestionAsync();

        await _sut.AnswerAsync(Code, Bob, q, "Fixed the flaky test");

        var bobsView = await BoardAsync(Bob);
        bobsView.People.Should().ContainSingle().Which.IsMe.Should().BeTrue();
        bobsView.People[0].Answers.Single().Text.Should().Be("Fixed the flaky test");
    }

    [Fact]
    public async Task Your_own_standup_comes_first()
    {
        await SeedAsync();
        var q = await FirstQuestionAsync();
        await _sut.AnswerAsync(Code, Alice, q, "Alice's answer");
        await _sut.AnswerAsync(Code, Bob, q, "Bob's answer");

        (await BoardAsync(Bob)).People[0].IsMe.Should().BeTrue();
    }

    [Fact]
    public async Task The_count_of_who_has_posted_is_always_visible()
    {
        // Visible even before you post, so you know whether you are early or last.
        await SeedAsync();
        var q = await FirstQuestionAsync();
        await _sut.AnswerAsync(Code, Alice, q, "Shipped the export");

        var bobsView = await BoardAsync(Bob);

        bobsView.PostedCount.Should().Be(1);
        bobsView.ParticipantCount.Should().Be(2);
    }

    [Fact]
    public async Task The_snapshot_never_says_who_has_not_posted()
    {
        // Presence only knows who opened the room, so "Dave hasn't posted" would as often mean
        // "Dave is on holiday". A roster needs accounts, which the platform does not have (#36).
        await SeedAsync();
        var q = await FirstQuestionAsync();
        await _sut.AnswerAsync(Code, Alice, q, "Shipped the export");

        var board = await BoardAsync(Alice);

        board.People.Should().OnlyContain(p => p.Answers.Count > 0,
            "only people who posted appear at all — absence is not reported as a name");
    }

    [Fact]
    public async Task One_answer_to_any_question_counts_as_having_posted()
    {
        // "Contribute before you read", not "fill in the form completely".
        await SeedAsync();
        var board = await BoardAsync();
        await _sut.AnswerAsync(Code, Bob, board.Questions[2].Id, "Nothing in my way");

        (await BoardAsync(Bob)).IHavePosted.Should().BeTrue();
    }

    // --- Answering ----------------------------------------------------------

    [Fact]
    public async Task An_answer_can_be_edited_and_the_edit_is_visible()
    {
        // A late edit should not be invisible to people who already read it.
        await SeedAsync();
        var q = await FirstQuestionAsync();
        await _sut.AnswerAsync(Code, Bob, q, "Fixed the flaky test");
        _clock.Advance(TimeSpan.FromMinutes(5));

        await _sut.AnswerAsync(Code, Bob, q, "Fixed the flaky test, and the slow one");

        var answer = (await BoardAsync(Bob)).People[0].Answers.Single();
        answer.Text.Should().Be("Fixed the flaky test, and the slow one");
        answer.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Clearing_an_answer_removes_it()
    {
        // Someone with nothing for a question should be able to say so without a placeholder.
        await SeedAsync();
        var board = await BoardAsync();
        await _sut.AnswerAsync(Code, Bob, board.Questions[0].Id, "Something");
        await _sut.AnswerAsync(Code, Bob, board.Questions[1].Id, "Another thing");

        await _sut.AnswerAsync(Code, Bob, board.Questions[0].Id, "");

        (await BoardAsync(Bob)).People[0].Answers.Should().ContainSingle();
    }

    [Fact]
    public async Task Clearing_every_answer_takes_you_back_to_not_having_posted()
    {
        // Honest rather than convenient: if you have contributed nothing, you read nothing.
        await SeedAsync();
        var q = await FirstQuestionAsync();
        await _sut.AnswerAsync(Code, Bob, q, "Something");
        (await BoardAsync(Bob)).IHavePosted.Should().BeTrue();

        await _sut.AnswerAsync(Code, Bob, q, "   ");

        (await BoardAsync(Bob)).IHavePosted.Should().BeFalse();
    }

    [Fact]
    public async Task An_over_long_answer_is_refused()
    {
        await SeedAsync();
        var q = await FirstQuestionAsync();

        var result = await _sut.AnswerAsync(
            Code, Bob, q, new string('x', StandupService.MaxAnswerLength + 1));

        result.Status.Should().Be(StandupActionStatus.InvalidAnswer);
    }

    [Fact]
    public async Task Answering_a_question_that_is_not_asked_is_refused()
    {
        await SeedAsync();

        (await _sut.AnswerAsync(Code, Bob, Guid.NewGuid(), "x")).Status
            .Should().Be(StandupActionStatus.QuestionNotFound);
    }

    [Fact]
    public async Task A_non_participant_cannot_answer()
    {
        await SeedAsync();
        var q = await FirstQuestionAsync();

        (await _sut.AnswerAsync(Code, "stranger", q, "Let me in")).Status
            .Should().Be(StandupActionStatus.NotParticipant);
    }

    [Fact]
    public async Task A_closed_standup_takes_no_more_answers()
    {
        await SeedAsync();
        var q = await FirstQuestionAsync();
        await _sut.CloseBoardAsync(Code, Alice);

        (await _sut.AnswerAsync(Code, Bob, q, "Too late")).Status
            .Should().NotBe(StandupActionStatus.Ok);
    }

    // --- Blockers -----------------------------------------------------------

    [Fact]
    public async Task A_blocker_can_be_raised_and_names_who_is_blocked()
    {
        await SeedAsync();

        var result = await _sut.AddBlockerAsync(Code, Bob, "Waiting on the platform team");

        result.Status.Should().Be(StandupActionStatus.Ok);
        var blocker = result.Board!.Blockers.Should().ContainSingle().Subject;
        blocker.Text.Should().Be("Waiting on the platform team");
        blocker.AuthorDisplayName.Should().Be("Bob");
        blocker.IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task Anyone_can_take_a_blocker_on()
    {
        // "Who is unblocking this" is the only decision a standup produces, so it is not
        // organiser-gated — whoever can help says so.
        await SeedAsync();
        await _sut.AddBlockerAsync(Code, Bob, "Waiting on the platform team");
        var id = (await BoardAsync()).Blockers.Single().Id;

        var result = await _sut.AssignBlockerAsync(Code, Alice, id, Alice, null);

        result.Board!.Blockers.Single().OwnerName.Should().Be("Alice");
    }

    [Fact]
    public async Task A_blocker_owner_may_be_someone_who_was_never_in_the_room()
    {
        // The shared ActionItemRules answer (#26/#35/#36) — its third consumer.
        await SeedAsync();
        await _sut.AddBlockerAsync(Code, Bob, "Waiting on the platform team");
        var id = (await BoardAsync()).Blockers.Single().Id;

        var result = await _sut.AssignBlockerAsync(Code, Alice, id, null, "Dana");

        result.Board!.Blockers.Single().OwnerName.Should().Be("Dana");
    }

    [Fact]
    public async Task A_blocker_can_be_cleared_and_put_back()
    {
        await SeedAsync();
        await _sut.AddBlockerAsync(Code, Bob, "Waiting on the platform team");
        var id = (await BoardAsync()).Blockers.Single().Id;

        var cleared = await _sut.ToggleBlockerResolvedAsync(Code, Alice, id);
        cleared.Board!.Blockers.Single().IsResolved.Should().BeTrue();

        var reopened = await _sut.ToggleBlockerResolvedAsync(Code, Alice, id);
        reopened.Board!.Blockers.Single().IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task A_blocker_stays_clearable_after_the_standup_closes()
    {
        // It gets unblocked hours after the standup, not during it — the same carve-out as retro
        // action items (#26).
        await SeedAsync();
        await _sut.AddBlockerAsync(Code, Bob, "Waiting on the platform team");
        var id = (await BoardAsync()).Blockers.Single().Id;
        await _sut.CloseBoardAsync(Code, Alice);

        var result = await _sut.ToggleBlockerResolvedAsync(Code, Alice, id);

        result.Status.Should().Be(StandupActionStatus.Ok);
        result.Board!.Blockers.Single().IsResolved.Should().BeTrue();
    }

    [Fact]
    public async Task Unresolved_blockers_come_first()
    {
        await SeedAsync();
        await _sut.AddBlockerAsync(Code, Bob, "First");
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _sut.AddBlockerAsync(Code, Bob, "Second");
        var first = (await BoardAsync()).Blockers.First(b => b.Text == "First").Id;

        await _sut.ToggleBlockerResolvedAsync(Code, Alice, first);

        (await BoardAsync()).Blockers.Select(b => b.Text).Should().Equal("Second", "First");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_blocker_is_refused(string text)
    {
        await SeedAsync();

        (await _sut.AddBlockerAsync(Code, Bob, text)).Status
            .Should().Be(StandupActionStatus.InvalidBlockerText);
    }

    [Fact]
    public async Task An_over_long_blocker_is_refused()
    {
        await SeedAsync();

        var result = await _sut.AddBlockerAsync(
            Code, Bob, new string('x', StandupService.MaxBlockerLength + 1));

        result.Status.Should().Be(StandupActionStatus.InvalidBlockerText);
    }

    [Fact]
    public async Task A_blocker_can_be_removed()
    {
        await SeedAsync();
        await _sut.AddBlockerAsync(Code, Bob, "Waiting on the platform team");
        var id = (await BoardAsync()).Blockers.Single().Id;

        (await _sut.DeleteBlockerAsync(Code, Bob, id)).Board!.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task Operating_on_a_blocker_that_is_not_there_is_reported_as_not_found()
    {
        await SeedAsync();
        var ghost = Guid.NewGuid();

        (await _sut.AssignBlockerAsync(Code, Alice, ghost, Alice, null)).Status
            .Should().Be(StandupActionStatus.BlockerNotFound);
        (await _sut.ToggleBlockerResolvedAsync(Code, Alice, ghost)).Status
            .Should().Be(StandupActionStatus.BlockerNotFound);
        (await _sut.DeleteBlockerAsync(Code, Alice, ghost)).Status
            .Should().Be(StandupActionStatus.BlockerNotFound);
    }

    [Fact]
    public async Task Blockers_are_visible_before_you_have_posted()
    {
        // Unlike answers. A blocker is a request for help, and gating it behind posting would keep
        // the one thing worth acting on from the person who could act.
        await SeedAsync();
        await _sut.AddBlockerAsync(Code, Alice, "Waiting on the platform team");

        var bobsView = await BoardAsync(Bob);

        bobsView.IHavePosted.Should().BeFalse();
        bobsView.Blockers.Should().ContainSingle();
    }

    // --- Carry-over ---------------------------------------------------------

    [Fact]
    public async Task Tomorrows_standup_carries_the_questions_and_the_open_blockers()
    {
        // Each day is its own room (#36): there is no recurrence, so this is how "the same standup
        // every morning" exists at all — the retro's carry-over pattern (#27), not a new concept.
        var codes = new SequentialShortCodes();
        var sut = TestServices.Standup(_store, codes, _clock);
        await sut.CreateAsync(new CreateStandupRequest(
            "Monday", Alice, "Alice", Organise: true, Questions: ["Wins?", "Worries?"]));
        await sut.AddBlockerAsync("standup-1", Alice, "Waiting on the platform team");
        await sut.AddBlockerAsync("standup-1", Alice, "Already sorted");
        var sorted = (await sut.GetByShortCodeAsync("standup-1", Alice))!
            .Blockers.First(b => b.Text == "Already sorted").Id;
        await sut.ToggleBlockerResolvedAsync("standup-1", Alice, sorted);

        await sut.CreateAsync(new CreateStandupRequest(
            "Tuesday", Alice, "Alice", Organise: true, PreviousBoardShortCode: "standup-1"));

        var tuesday = (await sut.GetByShortCodeAsync("standup-2", Alice))!;
        tuesday.Questions.Select(q => q.Text).Should().Equal("Wins?", "Worries?");
        tuesday.Blockers.Should().ContainSingle()
            .Which.Text.Should().Be("Waiting on the platform team");
        tuesday.Blockers[0].CarriedOver.Should().BeTrue();
        tuesday.PreviousBoardShortCode.Should().Be("standup-1");
    }

    [Fact]
    public async Task Carried_blockers_are_copies_so_the_new_standup_is_self_contained()
    {
        // It has to survive the previous room's retention delete (#15), exactly as a carried retro
        // action does (#27).
        var codes = new SequentialShortCodes();
        var sut = TestServices.Standup(_store, codes, _clock);
        await sut.CreateAsync(new CreateStandupRequest("Monday", Alice, "Alice", Organise: true));
        await sut.AddBlockerAsync("standup-1", Alice, "Waiting on the platform team");
        await sut.CreateAsync(new CreateStandupRequest(
            "Tuesday", Alice, "Alice", Organise: true, PreviousBoardShortCode: "standup-1"));

        await sut.DeleteBoardAsync("standup-1", Alice);

        (await sut.GetByShortCodeAsync("standup-2", Alice))!.Blockers.Should().ContainSingle();
    }

    [Fact]
    public async Task Explicit_questions_win_over_carried_ones()
    {
        var codes = new SequentialShortCodes();
        var sut = TestServices.Standup(_store, codes, _clock);
        await sut.CreateAsync(new CreateStandupRequest(
            "Monday", Alice, "Alice", Organise: true, Questions: ["Wins?"]));

        await sut.CreateAsync(new CreateStandupRequest(
            "Tuesday", Alice, "Alice", Organise: true,
            Questions: ["Something new?"], PreviousBoardShortCode: "standup-1"));

        (await sut.GetByShortCodeAsync("standup-2", Alice))!
            .Questions.Should().ContainSingle().Which.Text.Should().Be("Something new?");
    }

    [Fact]
    public async Task An_unknown_previous_code_is_an_error_rather_than_a_silent_no_op()
    {
        // Creating it anyway with nothing carried would look exactly like a team with no blockers.
        var result = await _sut.CreateAsync(new CreateStandupRequest(
            "Tuesday", Alice, "Alice", Organise: true, PreviousBoardShortCode: "no-such-standup"));

        result.Status.Should().Be(CreateStandupStatus.PreviousBoardNotFound);
        result.Board.Should().BeNull();
    }

    [Fact]
    public async Task Carrying_from_a_protected_standup_needs_its_password()
    {
        // A short code is a bearer token; without this, carry-over would be a way to read a
        // protected standup's blockers (#27's reasoning).
        var codes = new SequentialShortCodes();
        var sut = TestServices.Standup(_store, codes, _clock);
        await sut.CreateAsync(new CreateStandupRequest(
            "Monday", Alice, "Alice", Organise: true, Password: "hunter2"));
        await sut.AddBlockerAsync("standup-1", Alice, "Sensitive blocker");

        var refused = await sut.CreateAsync(new CreateStandupRequest(
            "Tuesday", Alice, "Alice", Organise: true, PreviousBoardShortCode: "standup-1"));
        var allowed = await sut.CreateAsync(new CreateStandupRequest(
            "Tuesday", Alice, "Alice", Organise: true,
            PreviousBoardShortCode: "standup-1", PreviousBoardPassword: "hunter2"));

        refused.Status.Should().Be(CreateStandupStatus.PreviousBoardPasswordRequired);
        allowed.Status.Should().Be(CreateStandupStatus.Ok);
    }

    [Fact]
    public async Task A_failed_carry_over_leaves_no_half_made_standup_behind()
    {
        var before = (await _store.GetAllAsync()).Count;

        await _sut.CreateAsync(new CreateStandupRequest(
            "Tuesday", Alice, "Alice", Organise: true, PreviousBoardShortCode: "no-such-standup"));

        (await _store.GetAllAsync()).Count.Should().Be(before);
    }

    // --- Room-level ---------------------------------------------------------

    [Fact]
    public async Task Joining_a_standup_room_over_another_tool_is_refused()
    {
        var poker = TestServices.Poker(_store, new StubShortCodeGenerator(Code), _clock);
        await poker.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, Alice, "Alice", Organise: true));

        (await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter)))
            .Status.Should().Be(JoinStatus.WrongTool);
    }

    [Fact]
    public async Task Leaving_and_presence_project_the_board_back()
    {
        await SeedAsync();

        var left = await _sut.LeaveAsync(Code, Bob);
        left.Board!.Room.Participants.Should().OnlyContain(p => p.UserId == Alice);

        var away = await _sut.MarkDisconnectedAsync(Code, Alice);
        away.Board!.Room.Participants.Single().IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Only_an_organiser_closes_or_deletes()
    {
        await SeedAsync();

        (await _sut.CloseBoardAsync(Code, Bob)).Status.Should().Be(StandupActionStatus.NotOrganiser);
        (await _sut.DeleteBoardAsync(Code, Bob)).Status.Should().Be(StandupActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task Deleting_leaves_nothing_to_project()
    {
        await SeedAsync();

        var result = await _sut.DeleteBoardAsync(Code, Alice);

        result.Status.Should().Be(StandupActionStatus.Ok);
        result.Board.Should().BeNull();
        (await _sut.GetByShortCodeAsync(Code, Alice)).Should().BeNull();
    }

    [Fact]
    public async Task Organiser_succession_and_reactions_are_inherited_from_the_room_engine()
    {
        await SeedAsync();

        var promoted = await _sut.PromoteToOrganiserAsync(Code, Alice, Bob);
        promoted.Board!.Room.Participants.Single(p => p.UserId == Bob).IsOrganiser.Should().BeTrue();

        var transferred = await _sut.TransferOrganiserAsync(Code, Alice, Bob);
        transferred.Board!.Room.Participants.Single(p => p.UserId == Alice)
            .IsOrganiser.Should().BeFalse();

        await _sut.SetReactionsEnabledAsync(Code, Bob, false);
        (await _sut.AreReactionsEnabledAsync(Code)).Should().BeFalse();
    }

    [Fact]
    public async Task An_organiser_can_set_and_clear_the_password()
    {
        var sut = TestServices.Standup(
            _store, new StubShortCodeGenerator(Code), _clock, new Security.Pbkdf2PasswordHasher());
        await sut.CreateAsync(new CreateStandupRequest(
            "Monday standup", Alice, "Alice", Organise: true));

        await sut.SetPasswordAsync(Code, Alice, "hunter2");
        (await sut.GetByShortCodeAsync(Code, Alice))!.Room.HasPassword.Should().BeTrue();

        await sut.SetPasswordAsync(Code, Alice, null);
        (await sut.GetByShortCodeAsync(Code, Alice))!.Room.HasPassword.Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_room_is_reported_as_not_found_for_every_write()
    {
        var ghost = Guid.NewGuid();

        (await _sut.AnswerAsync("no-such-room", Bob, ghost, "x")).Status
            .Should().Be(StandupActionStatus.BoardNotFound);
        (await _sut.AddBlockerAsync("no-such-room", Bob, "x")).Status
            .Should().Be(StandupActionStatus.BoardNotFound);
        (await _sut.ToggleBlockerResolvedAsync("no-such-room", Bob, ghost)).Status
            .Should().Be(StandupActionStatus.BoardNotFound);
        (await _sut.LeaveAsync("no-such-room", Bob)).Status
            .Should().Be(StandupActionStatus.BoardNotFound);
    }

    [Fact]
    public async Task Reading_a_room_that_is_not_a_standup_gives_nothing()
    {
        var poker = TestServices.Poker(_store, new StubShortCodeGenerator(Code), _clock);
        await poker.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, Alice, "Alice", Organise: true));

        (await _sut.GetByShortCodeAsync(Code, Alice)).Should().BeNull();
    }

    [Fact]
    public async Task A_non_participant_cannot_touch_a_blocker()
    {
        await SeedAsync();
        await _sut.AddBlockerAsync(Code, Bob, "Waiting on the platform team");
        var id = (await BoardAsync()).Blockers.Single().Id;

        (await _sut.ToggleBlockerResolvedAsync(Code, "stranger", id)).Status
            .Should().Be(StandupActionStatus.NotParticipant);
        (await _sut.DeleteBlockerAsync(Code, "stranger", id)).Status
            .Should().Be(StandupActionStatus.NotParticipant);
    }

    [Fact]
    public async Task A_blocker_write_against_another_tools_room_finds_no_board()
    {
        // The room exists and the short code is right — it is simply not a standup. Distinguishing
        // this from "no such room" would leak which codes are in use, so both read as not found.
        var poker = TestServices.Poker(_store, new StubShortCodeGenerator(Code), _clock);
        await poker.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, Alice, "Alice", Organise: true));

        (await _sut.ToggleBlockerResolvedAsync(Code, Alice, Guid.NewGuid())).Status
            .Should().Be(StandupActionStatus.BoardNotFound);
    }

    [Fact]
    public async Task A_closed_standup_takes_no_new_blockers_either()
    {
        // Clearing an existing one still works — raising a new one on yesterday's standup does not.
        await SeedAsync();
        await _sut.CloseBoardAsync(Code, Alice);

        (await _sut.AddBlockerAsync(Code, Bob, "Too late")).Status
            .Should().Be(StandupActionStatus.BoardClosed);
    }

    /// <summary>Hands out a fresh short code per room, so two standups can coexist.</summary>
    private sealed class SequentialShortCodes : IShortCodeGenerator
    {
        private int _next;
        public string Generate() => $"standup-{++_next}";
    }
}
