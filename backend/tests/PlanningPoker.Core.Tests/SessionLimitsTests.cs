using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

/// <summary>Abuse-protection: the per-session participant cap (#3-abuse).</summary>
public class SessionLimitsTests
{
    private const string Code = "blue-fox-42";

    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public SessionLimitsTests()
    {
        // A tiny cap of 2 makes the boundary easy to exercise.
        _sut = new SessionService(
            _store, new StubShortCodeGenerator(Code), _clock, limits: new SessionLimits { MaxParticipants = 2 });
    }

    private Task CreateAsync() =>
        _sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, "alice", "Alice", true));

    [Fact]
    public async Task A_new_joiner_beyond_the_cap_is_rejected_as_session_full()
    {
        await CreateAsync(); // Alice fills seat 1
        await _sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter)); // seat 2

        var result = await _sut.JoinAsync(new JoinSessionRequest(Code, "carol", "Carol", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.SessionFull);
    }

    [Fact]
    public async Task A_returning_participant_can_still_reconnect_when_the_session_is_full()
    {
        await CreateAsync();
        await _sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter)); // full at 2

        // Bob drops and reconnects with his own userId — he reclaims his seat, never blocked by the cap.
        await _sut.MarkDisconnectedAsync(Code, "bob");
        var result = await _sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.Ok);
    }

    [Fact]
    public async Task The_default_cap_allows_a_normal_sized_room()
    {
        var sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock); // default cap (100)
        await sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, "alice", "Alice", true));

        var result = await sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.Ok);
    }
}
