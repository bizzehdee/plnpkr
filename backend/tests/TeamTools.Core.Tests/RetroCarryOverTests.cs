using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// Carry-over (#27): the single deliberate link between two retro boards. "What happened to last
/// time's actions?" is the highest-value two minutes of a retro, and rooms are otherwise
/// independent — there is no team entity — so the link has to be explicit.
/// </summary>
public class RetroCarryOverTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SequentialShortCodes _codes = new();
    private readonly RetroService _sut;

    /// <summary>Hands out a fresh short code per board, so two retros can coexist.</summary>
    private sealed class SequentialShortCodes : IShortCodeGenerator
    {
        private int _next;
        public string Generate() => $"retro-{++_next}";
    }

    public RetroCarryOverTests()
    {
        var rooms = new RoomService(_store, _codes, _clock);
        _sut = new RetroService(_store, rooms, _clock);
    }

    private const string Facilitator = "alice";
    private const string Bob = "bob";

    /// <summary>Creates a retro, walks it to Discuss and records the given actions.</summary>
    private async Task<string> SeedPreviousAsync(
        string? password = null, params (string Title, bool Done)[] actions)
    {
        var created = await _sut.CreateAsync(new CreateRetroRequest(
            "Last sprint's retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice",
            Organise: true, Password: password));
        var code = created.Board!.Room.ShortCode;

        await _sut.AdvancePhaseAsync(code, Facilitator);
        await _sut.AdvancePhaseAsync(code, Facilitator);
        await _sut.AdvancePhaseAsync(code, Facilitator); // → Discuss

        foreach (var (title, done) in actions)
        {
            var added = await _sut.AddActionAsync(code, Facilitator, title, null, null, null, null);
            if (done)
            {
                var id = added.Board!.Actions.Single(a => a.Title == title).Id;
                await _sut.ToggleActionDoneAsync(code, Facilitator, id);
            }
        }

        return code;
    }

    private Task<CreateRetroResult> CreateNextAsync(string? previousCode, string? previousPassword = null) =>
        _sut.CreateAsync(new CreateRetroRequest(
            "This sprint's retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice",
            Organise: true, PreviousBoardShortCode: previousCode,
            PreviousBoardPassword: previousPassword));

    // --- What carries --------------------------------------------------

    [Fact]
    public async Task Unfinished_actions_carry_forward()
    {
        var previous = await SeedPreviousAsync(null, ("Speed up CI", false), ("Book the room", false));

        var result = await CreateNextAsync(previous);

        result.Status.Should().Be(CreateRetroStatus.Ok);
        result.Board!.Actions.Select(a => a.Title)
            .Should().BeEquivalentTo("Speed up CI", "Book the room");
    }

    [Fact]
    public async Task Finished_actions_do_not_carry_forward()
    {
        // The point is what still needs doing, not a running history.
        var previous = await SeedPreviousAsync(null, ("Speed up CI", true), ("Book the room", false));

        var result = await CreateNextAsync(previous);

        result.Board!.Actions.Should().ContainSingle().Which.Title.Should().Be("Book the room");
    }

    [Fact]
    public async Task Carried_actions_are_marked_as_carried_over()
    {
        var previous = await SeedPreviousAsync(null, ("Speed up CI", false));

        var result = await CreateNextAsync(previous);

        result.Board!.Actions[0].CarriedOver.Should().BeTrue();
    }

    [Fact]
    public async Task Carried_actions_keep_their_owner_and_due_date()
    {
        var created = await _sut.CreateAsync(new CreateRetroRequest(
            "Previous", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        var code = created.Board!.Room.ShortCode;
        await _sut.JoinAsync(new JoinSessionRequest(code, Bob, "Bob", ParticipantRole.Voter));
        await _sut.AdvancePhaseAsync(code, Facilitator);
        await _sut.AdvancePhaseAsync(code, Facilitator);
        await _sut.AdvancePhaseAsync(code, Facilitator);
        var due = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await _sut.AddActionAsync(code, Facilitator, "Speed up CI", Bob, null, due, null);

        var result = await CreateNextAsync(code);

        var carried = result.Board!.Actions.Should().ContainSingle().Subject;
        carried.OwnerUserId.Should().Be(Bob);
        carried.OwnerName.Should().Be("Bob");
        carried.DueDate.Should().Be(due);
    }

    [Fact]
    public async Task A_carried_action_is_a_copy_with_its_own_identity()
    {
        var previous = await SeedPreviousAsync(null, ("Speed up CI", false));
        var before = (await _sut.GetByShortCodeAsync(previous, Facilitator))!.Actions[0].Id;

        var result = await CreateNextAsync(previous);

        result.Board!.Actions[0].Id.Should().NotBe(before);
    }

    [Fact]
    public async Task The_new_board_records_which_retro_it_carried_from()
    {
        var previous = await SeedPreviousAsync(null, ("Speed up CI", false));

        var result = await CreateNextAsync(previous);

        result.Board!.PreviousBoardShortCode.Should().Be(previous);
    }

    [Fact]
    public async Task Marking_a_carried_action_done_does_not_touch_the_original()
    {
        // Copies, not references: the two boards are independent from the moment they are linked.
        var previous = await SeedPreviousAsync(null, ("Speed up CI", false));
        var next = await CreateNextAsync(previous);
        var nextCode = next.Board!.Room.ShortCode;

        await _sut.ToggleActionDoneAsync(nextCode, Facilitator, next.Board.Actions[0].Id);

        (await _sut.GetByShortCodeAsync(previous, Facilitator))!.Actions[0].IsDone
            .Should().BeFalse("the previous retro's record is its own");
    }

    [Fact]
    public async Task Deleting_the_previous_board_leaves_the_carried_actions_intact()
    {
        // This is why they are copies: the old board will eventually be retention-deleted (#15),
        // and the new board must not lose its commitments when that happens.
        var previous = await SeedPreviousAsync(null, ("Speed up CI", false));
        var next = await CreateNextAsync(previous);
        var nextCode = next.Board!.Room.ShortCode;

        await _sut.DeleteBoardAsync(previous, Facilitator);

        var board = (await _sut.GetByShortCodeAsync(nextCode, Facilitator))!;
        board.Actions.Should().ContainSingle().Which.Title.Should().Be("Speed up CI");
    }

    [Fact]
    public async Task A_theme_reference_does_not_carry_because_that_theme_belongs_to_the_old_board()
    {
        var created = await _sut.CreateAsync(new CreateRetroRequest(
            "Previous", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        var code = created.Board!.Room.ShortCode;
        var added = await _sut.AddCardAsync(code, Facilitator, created.Board.Columns[0].Id, "CI is slow");
        var cardId = added.Board!.Columns[0].Cards[0].Id;
        await _sut.AdvancePhaseAsync(code, Facilitator);
        var grouped = await _sut.GroupCardsAsync(code, Facilitator, [cardId], null);
        var groupId = grouped.Board!.Groups[0].Id;
        await _sut.AdvancePhaseAsync(code, Facilitator);
        await _sut.AdvancePhaseAsync(code, Facilitator);
        await _sut.AddActionAsync(code, Facilitator, "Speed up CI", null, null, null, groupId);

        var result = await CreateNextAsync(code);

        result.Board!.Actions[0].SourceGroupId.Should().BeNull();
    }

    // --- The password gate -------------------------------------------------

    [Fact]
    public async Task Carrying_from_a_protected_retro_needs_its_password()
    {
        // A short code is a bearer token here. Without this, carry-over would be a way to read a
        // protected board's commitments.
        var previous = await SeedPreviousAsync("hunter2", ("Said in confidence", false));

        var result = await CreateNextAsync(previous);

        result.Status.Should().Be(CreateRetroStatus.PreviousBoardPasswordRequired);
        result.Error.Should().Contain("password");
    }

    [Fact]
    public async Task The_wrong_password_is_refused()
    {
        var previous = await SeedPreviousAsync("hunter2", ("Said in confidence", false));

        var result = await CreateNextAsync(previous, "guess");

        result.Status.Should().Be(CreateRetroStatus.PreviousBoardPasswordRequired);
    }

    [Fact]
    public async Task The_right_password_carries_the_actions()
    {
        var previous = await SeedPreviousAsync("hunter2", ("Speed up CI", false));

        var result = await CreateNextAsync(previous, "hunter2");

        result.Status.Should().Be(CreateRetroStatus.Ok);
        result.Board!.Actions.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unknown_previous_retro_is_reported_rather_than_ignored()
    {
        // Silently creating a board with nothing carried would look like the actions had all been
        // finished.
        var result = await CreateNextAsync("no-such-retro");

        result.Status.Should().Be(CreateRetroStatus.PreviousBoardNotFound);
    }

    [Fact]
    public async Task Carrying_from_a_poker_session_is_refused()
    {
        var poker = TestServices.Poker(_store, _codes, _clock);
        var created = await poker.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, Facilitator, "Alice", Organise: true));

        var result = await CreateNextAsync(created.Session!.ShortCode);

        result.Status.Should().Be(CreateRetroStatus.PreviousBoardNotFound);
    }

    [Fact]
    public async Task A_failed_carry_over_leaves_no_half_made_retro_behind()
    {
        var before = (await _store.GetAllAsync()).Count;

        var result = await CreateNextAsync("no-such-retro");

        result.Status.Should().Be(CreateRetroStatus.PreviousBoardNotFound);
        (await _store.GetAllAsync()).Count.Should().Be(before);
    }

    // --- Creating without carry-over ---------------------------------------

    [Fact]
    public async Task A_retro_created_without_a_previous_code_carries_nothing()
    {
        var result = await CreateNextAsync(null);

        result.Status.Should().Be(CreateRetroStatus.Ok);
        result.Board!.Actions.Should().BeEmpty();
        result.Board.PreviousBoardShortCode.Should().BeNull();
    }

    [Fact]
    public async Task Carrying_from_a_retro_with_nothing_outstanding_is_fine()
    {
        // A team that finished everything should still be able to point at last time.
        var previous = await SeedPreviousAsync(null, ("All done", true));

        var result = await CreateNextAsync(previous);

        result.Status.Should().Be(CreateRetroStatus.Ok);
        result.Board!.Actions.Should().BeEmpty();
        result.Board.PreviousBoardShortCode.Should().Be(previous);
    }

    [Fact]
    public async Task Carried_actions_are_writable_on_the_new_board_from_the_start()
    {
        // They are the review list at the top of Collect (#27), so ticking one off must work before
        // the new retro has reached its own Actions phase.
        var previous = await SeedPreviousAsync(null, ("Speed up CI", false));
        var next = await CreateNextAsync(previous);
        var nextCode = next.Board!.Room.ShortCode;

        next.Board.Phase.Should().Be(RetroPhase.Collect);
        var result = await _sut.ToggleActionDoneAsync(
            nextCode, Facilitator, next.Board.Actions[0].Id);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Actions[0].IsDone.Should().BeTrue();
    }
}
