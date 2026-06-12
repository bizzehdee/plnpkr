using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class ResilienceTests
{
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    private const string Code = "blue-fox-42";
    private const string Organiser = "alice";
    private const string Bob = "bob";

    public ResilienceTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private async Task SeedAsync(bool organise = true)
    {
        await _sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, Organiser, "Alice", organise));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
    }

    private async Task<SessionSnapshot> SnapAsync() => (await _sut.GetByShortCodeAsync(Code))!;
    private ParticipantInfo P(SessionSnapshot s, string id) => s.Participants.Single(p => p.UserId == id);

    // --- Disconnect keeps state -------------------------------------------

    [Fact]
    public async Task Disconnect_marks_away_but_keeps_the_participant_and_their_vote()
    {
        await SeedAsync();
        await _sut.CastVoteAsync(Code, Bob, "5");
        await _sut.RevealAsync(Code, Organiser);

        var result = await _sut.MarkDisconnectedAsync(Code, Bob);

        result.Status.Should().Be(SessionActionStatus.Ok);
        var bob = P(result.Session!, Bob);
        bob.IsConnected.Should().BeFalse();
        bob.HasVoted.Should().BeTrue();
        bob.Vote.Should().Be("5"); // vote preserved across the drop
    }

    [Fact]
    public async Task Reconnect_reclaims_the_seat_and_marks_connected_again()
    {
        await SeedAsync();
        await _sut.CastVoteAsync(Code, Bob, "5");
        await _sut.MarkDisconnectedAsync(Code, Bob);

        var result = await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.Ok);
        var bob = P(result.Session!, Bob);
        bob.IsConnected.Should().BeTrue();
        result.Session!.Participants.Should().HaveCount(2); // not duplicated
    }

    [Fact]
    public async Task Organiser_reclaims_their_role_on_reconnect_even_after_eviction()
    {
        await SeedAsync(); // Alice is organiser

        // Simulate Alice having been evicted while away: re-join as a "new" participant.
        var store = await _store.FindByShortCodeAsync(Code);
        store!.Participants.RemoveAll(p => p.UserId == Organiser);

        var result = await _sut.JoinAsync(new JoinSessionRequest(Code, Organiser, "Alice", ParticipantRole.Observer));

        P(result.Session!, Organiser).IsOrganiser.Should().BeTrue("OrganiserUserId still points at Alice");
        result.Session!.OrganiserUserId.Should().Be(Organiser);
    }

    // --- Organiser leaves --------------------------------------------------

    [Fact]
    public async Task Organiser_leaving_clears_the_organiser_so_anyone_can_then_control()
    {
        await SeedAsync();

        await _sut.LeaveAsync(Code, Organiser);

        var snap = await SnapAsync();
        snap.OrganiserUserId.Should().BeNull();
        // Bob (a remaining participant) can now reveal under the no-organiser fallback.
        var reveal = await _sut.RevealAsync(Code, Bob);
        reveal.Status.Should().Be(SessionActionStatus.Ok);
    }

    // --- Auto-reveal ignores disconnected voters --------------------------

    [Fact]
    public async Task Auto_reveal_fires_when_all_connected_voters_have_voted_ignoring_an_away_voter()
    {
        await SeedAsync(); // Alice organiser-observer, Bob voter
        await _sut.JoinAsync(new JoinSessionRequest(Code, "carol", "Carol", ParticipantRole.Voter));
        await _sut.SetAutoRevealAsync(Code, Organiser, true);

        // Carol drops without voting; Bob is the only connected voter.
        await _sut.MarkDisconnectedAsync(Code, "carol");

        var result = await _sut.CastVoteAsync(Code, Bob, "5");

        result.Session!.State.Should().Be(SessionState.Revealed, "Carol is away so doesn't block the gate");
    }
}
