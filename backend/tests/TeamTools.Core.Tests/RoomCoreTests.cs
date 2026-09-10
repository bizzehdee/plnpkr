using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Poker;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// The seams #19 introduced: the room/tool split itself. A room is one tool and only one; the room
/// fragment is the single definition of room-level snapshot state; and room-level changes reach the
/// tool through an explicit hook rather than the room engine knowing about rounds.
/// </summary>
public class RoomCoreTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly PokerService _poker;
    private readonly RoomService _rooms;

    public RoomCoreTests()
    {
        _rooms = TestServices.Rooms(_store, new StubShortCodeGenerator(Code), _clock);
        _poker = TestServices.Poker(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Organiser = "alice";

    private Task SeedAsync(string? password = null) =>
        _poker.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, Organiser, "Alice", Organise: true, Password: password));

    // --- One tool per room -------------------------------------------------

    [Fact]
    public async Task A_created_poker_room_is_marked_as_the_poker_tool()
    {
        await SeedAsync();

        var room = await _store.FindByShortCodeAsync(Code);

        room!.Tool.Should().Be(RoomTool.Poker);
        room.PokerRound.Should().NotBeNull("a poker room is created with its round in the same write");
    }

    [Fact]
    public async Task Poker_operations_refuse_a_room_that_hosts_another_tool()
    {
        // A retro room reached by a poker code should never happen — the join page routes by tool —
        // so this is a programming error, not a user-facing status. Assert it fails loudly rather
        // than silently mutating a room of the wrong kind.
        var retro = await _rooms.NewRoomAsync(
            RoomTool.Retro, "Retro", Organiser, "Alice", organise: true, enableReactions: true, password: null);
        await _rooms.AddAsync(retro);

        var act = () => _poker.RevealAsync(retro.ShortCode, Organiser);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*hosts Retro, not Poker*");
    }

    // --- The shared room fragment ------------------------------------------

    [Fact]
    public async Task The_snapshot_carries_room_level_state_in_the_room_fragment()
    {
        await SeedAsync(password: "hunter2");

        var snapshot = (await _poker.GetByShortCodeAsync(Code))!;

        snapshot.Room.ShortCode.Should().Be(Code);
        snapshot.Room.Name.Should().Be("Sprint");
        snapshot.Room.Tool.Should().Be(RoomTool.Poker);
        snapshot.Room.OrganiserUserId.Should().Be(Organiser);
        snapshot.Room.IsClosed.Should().BeFalse();
        snapshot.Room.Participants.Should().ContainSingle().Which.UserId.Should().Be(Organiser);
    }

    [Fact]
    public async Task The_room_fragment_reports_whether_a_password_is_set_but_never_the_hash()
    {
        await SeedAsync(password: "hunter2");

        var withPassword = (await _poker.GetByShortCodeAsync(Code))!;
        withPassword.Room.HasPassword.Should().BeTrue();

        await _poker.SetPasswordAsync(Code, Organiser, null);

        var cleared = (await _poker.GetByShortCodeAsync(Code))!;
        cleared.Room.HasPassword.Should().BeFalse();
    }

    [Fact]
    public async Task Closing_a_room_shows_in_the_room_fragment()
    {
        await SeedAsync();

        var result = await _poker.CloseSessionAsync(Code, Organiser);

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.Room.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task Closing_a_room_stops_a_running_countdown_so_it_cannot_reveal_a_frozen_room()
    {
        await SeedAsync();
        await _poker.StartTimerAsync(Code, Organiser, 60);

        var closed = await _poker.CloseSessionAsync(Code, Organiser);

        closed.Session!.TimerDeadline.Should().BeNull();
    }

    [Fact]
    public async Task Closing_an_already_closed_room_is_idempotent()
    {
        await SeedAsync();
        await _poker.CloseSessionAsync(Code, Organiser);
        var closedAt = (await _store.FindByShortCodeAsync(Code))!.ClosedAt;

        _clock.Advance(TimeSpan.FromMinutes(5));
        var again = await _poker.CloseSessionAsync(Code, Organiser);

        again.Status.Should().Be(SessionActionStatus.Ok);
        (await _store.FindByShortCodeAsync(Code))!.ClosedAt.Should().Be(closedAt);
    }

    // --- The landing read --------------------------------------------------

    [Fact]
    public async Task The_landing_read_says_which_tool_a_short_code_belongs_to()
    {
        await SeedAsync(password: "hunter2");

        var landing = await _rooms.GetLandingAsync(Code);

        landing!.Tool.Should().Be(RoomTool.Poker, "the join page routes on this");
        landing.Name.Should().Be("Sprint");
        landing.RequiresPassword.Should().BeTrue();
    }

    [Fact]
    public async Task The_landing_read_is_null_for_an_unknown_short_code()
    {
        (await _rooms.GetLandingAsync("no-such-room")).Should().BeNull();
    }

    // --- The tool hook -----------------------------------------------------

    [Fact]
    public async Task A_room_level_role_change_re_evaluates_the_tools_completion_gate()
    {
        // The room engine doesn't know what auto-reveal is; poker passes it in as a hook. Making the
        // last unvoted voter an observer must therefore still complete the round.
        await SeedAsync();
        await _poker.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter));
        await _poker.JoinAsync(new JoinSessionRequest(Code, "carol", "Carol", ParticipantRole.Voter));
        await _poker.SetAutoRevealAsync(Code, Organiser, true);
        await _poker.CastVoteAsync(Code, "bob", "5");

        var result = await _poker.ChangeRoleAsync(Code, Organiser, "carol", ParticipantRole.Observer);

        result.Session!.State.Should().Be(SessionState.Revealed);
    }

    [Fact]
    public void The_auto_reveal_gate_ignores_a_room_with_no_round()
    {
        // Belt and braces for the hook: a retro room passed to the poker gate must be a no-op, not
        // a crash, because the room engine calls the hook for every room-level change.
        var retro = new Room { Id = Guid.NewGuid(), ShortCode = "r-1", Tool = RoomTool.Retro };

        var act = () => PokerRoundRules.MaybeAutoReveal(retro);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Transferring_facilitation_to_yourself_is_a_no_op()
    {
        await SeedAsync();

        var result = await _poker.TransferOrganiserAsync(Code, Organiser, Organiser);

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.Participants.Single(p => p.UserId == Organiser).IsOrganiser.Should().BeTrue();
    }

    [Fact]
    public async Task Transferring_facilitation_to_an_unknown_participant_is_rejected()
    {
        await SeedAsync();

        var result = await _poker.TransferOrganiserAsync(Code, Organiser, "nobody");

        result.Status.Should().Be(SessionActionStatus.TargetNotFound);
    }

    // --- The poker timer sweep ---------------------------------------------

    [Fact]
    public async Task The_timer_sweep_skips_rooms_that_have_no_poker_round()
    {
        // The store's query already filters to poker rooms; the service still guards, because a
        // room without a round is not something a sweep should throw over.
        var retro = await _rooms.NewRoomAsync(
            RoomTool.Retro, "Retro", Organiser, "Alice", organise: true, enableReactions: true, password: null);
        await _rooms.AddAsync(retro);

        var expired = await TestServices.Timers(_store, _clock).ExpireDueRoundTimersAsync();

        expired.Should().BeEmpty();
    }
}
