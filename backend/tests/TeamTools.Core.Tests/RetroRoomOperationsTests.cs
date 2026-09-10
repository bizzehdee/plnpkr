using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// The retro tool's room-level surface (#21). These are thin wrappers over <see cref="RoomService"/>
/// — which is the point: the retro tool gets presence, roles, organisers, the password and the
/// close/delete lifecycle from the shared engine. They are tested because the hub calls them, and a
/// wrapper that projects the wrong status or forgets the snapshot is invisible until a user hits it.
/// </summary>
public class RetroRoomOperationsTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly RetroService _sut;

    public RetroRoomOperationsTests()
    {
        var rooms = TestServices.Rooms(_store, new StubShortCodeGenerator(Code), _clock);
        _sut = new RetroService(_store, rooms, _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Facilitator = "alice";
    private const string Bob = "bob";

    private async Task SeedAsync()
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
    }

    private async Task<RetroBoardSnapshot> BoardAsync(string forUserId) =>
        (await _sut.GetByShortCodeAsync(Code, forUserId))!;

    // --- Leaving ------------------------------------------------------------

    [Fact]
    public async Task Leaving_removes_the_seat_and_returns_the_updated_board()
    {
        await SeedAsync();

        var result = await _sut.LeaveAsync(Code, Bob);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Room.Participants.Select(p => p.UserId).Should().NotContain(Bob);
    }

    [Fact]
    public async Task Leaving_a_board_that_does_not_exist_is_reported_as_not_found()
    {
        var result = await _sut.LeaveAsync("no-such-board", Bob);

        result.Status.Should().Be(RetroActionStatus.BoardNotFound);
    }

    [Fact]
    public async Task A_disconnect_keeps_the_seat_so_a_reconnect_can_reclaim_it()
    {
        await SeedAsync();

        var result = await _sut.MarkDisconnectedAsync(Code, Bob);

        result.Status.Should().Be(RetroActionStatus.Ok);
        var bob = result.Board!.Room.Participants.Single(p => p.UserId == Bob);
        bob.IsConnected.Should().BeFalse();
    }

    // --- Roles --------------------------------------------------------------

    [Fact]
    public async Task An_organiser_can_make_a_participant_an_observer()
    {
        // Voter/Observer is room-level and meaningful in a retro too: an observer watches.
        await SeedAsync();

        var result = await _sut.ChangeRoleAsync(Code, Facilitator, Bob, ParticipantRole.Observer);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Room.Participants.Single(p => p.UserId == Bob).Role
            .Should().Be(ParticipantRole.Observer);
    }

    [Fact]
    public async Task A_participant_cannot_change_someone_elses_role()
    {
        await SeedAsync();
        await _sut.JoinAsync(new JoinSessionRequest(Code, "carol", "Carol", ParticipantRole.Voter));

        var result = await _sut.ChangeRoleAsync(Code, Bob, "carol", ParticipantRole.Observer);

        result.Status.Should().Be(RetroActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task Self_service_role_changes_can_be_turned_off_by_the_facilitator()
    {
        await SeedAsync();
        await _sut.SetAllowRoleChangeAsync(Code, Facilitator, false);

        var result = await _sut.ChangeRoleAsync(Code, Bob, Bob, ParticipantRole.Observer);

        // RoleChangeDisabled has no retro-specific status, so it surfaces as a plain refusal.
        result.Status.Should().NotBe(RetroActionStatus.Ok);
        (await BoardAsync(Bob)).Room.Participants.Single(p => p.UserId == Bob).Role
            .Should().Be(ParticipantRole.Voter);
    }

    // --- Organisers ---------------------------------------------------------

    [Fact]
    public async Task An_organiser_can_promote_and_demote_a_co_facilitator()
    {
        await SeedAsync();

        var promoted = await _sut.PromoteToOrganiserAsync(Code, Facilitator, Bob);
        promoted.Board!.Room.Participants.Single(p => p.UserId == Bob).IsOrganiser.Should().BeTrue();

        var demoted = await _sut.DemoteOrganiserAsync(Code, Facilitator, Bob);
        demoted.Board!.Room.Participants.Single(p => p.UserId == Bob).IsOrganiser.Should().BeFalse();
    }

    [Fact]
    public async Task Facilitation_can_be_handed_over_in_one_move()
    {
        await SeedAsync();

        var result = await _sut.TransferOrganiserAsync(Code, Facilitator, Bob);

        result.Status.Should().Be(RetroActionStatus.Ok);
        var seats = result.Board!.Room.Participants;
        seats.Single(p => p.UserId == Bob).IsOrganiser.Should().BeTrue();
        seats.Single(p => p.UserId == Facilitator).IsOrganiser.Should().BeFalse();
    }

    [Fact]
    public async Task A_participant_cannot_promote_themselves()
    {
        await SeedAsync();

        var result = await _sut.PromoteToOrganiserAsync(Code, Bob, Bob);

        result.Status.Should().Be(RetroActionStatus.NotOrganiser);
    }

    // --- Password & reactions -----------------------------------------------

    [Fact]
    public async Task An_organiser_can_set_and_clear_the_board_password()
    {
        await SeedAsync();

        var set = await _sut.SetPasswordAsync(Code, Facilitator, "hunter2");
        set.Board!.Room.HasPassword.Should().BeTrue();

        var cleared = await _sut.SetPasswordAsync(Code, Facilitator, null);
        cleared.Board!.Room.HasPassword.Should().BeFalse();
    }

    [Fact]
    public async Task Reactions_can_be_turned_off_for_a_board()
    {
        await SeedAsync();

        var result = await _sut.SetReactionsEnabledAsync(Code, Facilitator, false);

        result.Board!.Room.ReactionsEnabled.Should().BeFalse();
        (await _sut.AreReactionsEnabledAsync(Code)).Should().BeFalse();
    }

    [Fact]
    public async Task Reactions_are_reported_as_off_for_an_unknown_board()
    {
        (await _sut.AreReactionsEnabledAsync("no-such-board")).Should().BeFalse();
    }

    // --- Lifecycle ----------------------------------------------------------

    [Fact]
    public async Task Closing_a_board_marks_it_read_only()
    {
        await SeedAsync();

        var result = await _sut.CloseBoardAsync(Code, Facilitator);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Room.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task A_participant_cannot_close_the_board()
    {
        await SeedAsync();

        var result = await _sut.CloseBoardAsync(Code, Bob);

        result.Status.Should().Be(RetroActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task Room_operations_on_an_unknown_board_report_not_found()
    {
        var result = await _sut.SetReactionsEnabledAsync("no-such-board", Facilitator, false);

        result.Status.Should().Be(RetroActionStatus.BoardNotFound);
    }

    [Fact]
    public async Task Room_operations_from_a_non_participant_are_refused()
    {
        await SeedAsync();

        var result = await _sut.CloseBoardAsync(Code, "stranger");

        result.Status.Should().Be(RetroActionStatus.NotParticipant);
    }

    [Fact]
    public async Task A_closed_board_still_refuses_edits_to_existing_cards()
    {
        await SeedAsync();
        var board = await BoardAsync(Bob);
        var added = await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "before the close");
        var cardId = added.Board!.Columns[0].Cards[0].Id;
        await _sut.CloseBoardAsync(Code, Facilitator);

        var edit = await _sut.EditCardAsync(Code, Bob, cardId, "after the close");
        var remove = await _sut.DeleteCardAsync(Code, Bob, cardId);

        edit.Status.Should().Be(RetroActionStatus.BoardClosed);
        remove.Status.Should().Be(RetroActionStatus.BoardClosed);
    }
}
