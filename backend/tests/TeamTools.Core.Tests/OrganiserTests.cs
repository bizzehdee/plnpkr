using FluentAssertions;
using TeamTools.Core;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>Multiple organisers and facilitator hand-off / succession (#7).</summary>
public class OrganiserTests
{
    private const string Code = "blue-fox-42";
    private const string Alice = "alice"; // founding organiser
    private const string Bob = "bob";
    private const string Carol = "carol";

    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public OrganiserTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private async Task SeedAsync()
    {
        await _sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, Alice, "Alice", true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Carol, "Carol", ParticipantRole.Voter));
    }

    private ParticipantInfo P(SessionSnapshot s, string id) => s.Participants.Single(p => p.UserId == id);

    [Fact]
    public async Task Organiser_can_promote_a_co_organiser_who_can_then_control()
    {
        await SeedAsync();

        var promote = await _sut.PromoteToOrganiserAsync(Code, Alice, Bob);
        promote.Status.Should().Be(SessionActionStatus.Ok);
        P(promote.Session!, Bob).IsOrganiser.Should().BeTrue();

        // Bob, now a co-organiser, can reveal.
        var reveal = await _sut.RevealAsync(Code, Bob);
        reveal.Status.Should().Be(SessionActionStatus.Ok);
    }

    [Fact]
    public async Task A_non_organiser_cannot_promote()
    {
        await SeedAsync();

        var result = await _sut.PromoteToOrganiserAsync(Code, Bob, Carol);

        result.Status.Should().Be(SessionActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task Promote_unknown_target_returns_target_not_found()
    {
        await SeedAsync();

        var result = await _sut.PromoteToOrganiserAsync(Code, Alice, "ghost");

        result.Status.Should().Be(SessionActionStatus.TargetNotFound);
    }

    [Fact]
    public async Task A_co_organiser_keeps_their_role_across_a_reconnect()
    {
        await SeedAsync();
        await _sut.PromoteToOrganiserAsync(Code, Alice, Bob);

        await _sut.MarkDisconnectedAsync(Code, Bob);
        var rejoin = await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));

        P(rejoin.Session!, Bob).IsOrganiser.Should().BeTrue("a promoted co-organiser keeps the role on reconnect");
    }

    [Fact]
    public async Task Demote_revokes_control_and_clears_the_founding_pointer()
    {
        await SeedAsync();
        await _sut.PromoteToOrganiserAsync(Code, Alice, Bob);

        // Alice (founding) demotes herself; Bob remains the organiser.
        var demote = await _sut.DemoteOrganiserAsync(Code, Alice, Alice);
        demote.Status.Should().Be(SessionActionStatus.Ok);
        P(demote.Session!, Alice).IsOrganiser.Should().BeFalse();

        // Alice can no longer control, but Bob still can.
        (await _sut.RevealAsync(Code, Alice)).Status.Should().Be(SessionActionStatus.NotOrganiser);
        (await _sut.ResetRoundAsync(Code, Bob)).Status.Should().Be(SessionActionStatus.Ok);
    }

    [Fact]
    public async Task Transfer_promotes_the_target_and_steps_the_caller_down()
    {
        await SeedAsync();

        var transfer = await _sut.TransferOrganiserAsync(Code, Alice, Bob);

        transfer.Status.Should().Be(SessionActionStatus.Ok);
        P(transfer.Session!, Bob).IsOrganiser.Should().BeTrue();
        P(transfer.Session!, Alice).IsOrganiser.Should().BeFalse();
        // The handed-off organiser now controls; the former one does not.
        (await _sut.RevealAsync(Code, Alice)).Status.Should().Be(SessionActionStatus.NotOrganiser);
        (await _sut.RevealAsync(Code, Bob)).Status.Should().Be(SessionActionStatus.Ok);
    }

    [Fact]
    public async Task Transfer_to_self_is_a_harmless_no_op()
    {
        await SeedAsync();

        var result = await _sut.TransferOrganiserAsync(Code, Alice, Alice);

        result.Status.Should().Be(SessionActionStatus.Ok);
        P(result.Session!, Alice).IsOrganiser.Should().BeTrue();
    }

    [Fact]
    public async Task When_the_only_organiser_disconnects_succession_promotes_the_longest_present_member()
    {
        await SeedAsync(); // Alice organiser (joined first), Bob, Carol

        var result = await _sut.MarkDisconnectedAsync(Code, Alice);

        // Bob (earliest non-organiser) inherits the facilitator role; Carol does not.
        P(result.Session!, Bob).IsOrganiser.Should().BeTrue();
        P(result.Session!, Carol).IsOrganiser.Should().BeFalse();
        (await _sut.RevealAsync(Code, Bob)).Status.Should().Be(SessionActionStatus.Ok);
    }

    [Fact]
    public async Task Succession_does_not_fire_while_another_organiser_is_still_connected()
    {
        await SeedAsync();
        await _sut.PromoteToOrganiserAsync(Code, Alice, Bob); // two organisers

        var result = await _sut.MarkDisconnectedAsync(Code, Alice); // Bob still connected

        // Carol must NOT be promoted — Bob is still an active organiser.
        P(result.Session!, Carol).IsOrganiser.Should().BeFalse();
    }

    [Fact]
    public async Task Succession_does_not_apply_to_an_open_no_organiser_session()
    {
        // Created without organising → open session (anyone controls); a disconnect promotes no one.
        await _sut.CreateAsync(new CreateSessionRequest("Open", DeckType.Fibonacci, null, Alice, "Alice", false));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));

        var result = await _sut.MarkDisconnectedAsync(Code, Alice);

        result.Session!.Participants.Should().OnlyContain(p => !p.IsOrganiser);
    }
}
