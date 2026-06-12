using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

/// <summary>Behaviour of organiser close (read-only) &amp; soft-delete (#26).</summary>
public class SessionLifecycleTests
{
    private const string Code = "blue-fox-42";
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public SessionLifecycleTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
    }

    /// <summary>Organiser = alice; bob joins as a voter.</summary>
    private async Task SeedAsync()
    {
        await _sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, "alice", "Alice", true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter, null));
    }

    // --- Close (read-only) ---

    [Fact]
    public async Task Close_marks_the_session_read_only()
    {
        await SeedAsync();

        var result = await _sut.CloseSessionAsync(Code, "alice");

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task A_closed_session_is_still_viewable()
    {
        await SeedAsync();
        await _sut.CloseSessionAsync(Code, "alice");

        var snapshot = await _sut.GetByShortCodeAsync(Code);

        snapshot.Should().NotBeNull();
        snapshot!.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task A_closed_session_rejects_mutations()
    {
        await SeedAsync();
        await _sut.CloseSessionAsync(Code, "alice");

        (await _sut.CastVoteAsync(Code, "bob", "5")).Status.Should().Be(SessionActionStatus.SessionClosed);
        (await _sut.RevealAsync(Code, "alice")).Status.Should().Be(SessionActionStatus.SessionClosed);
        (await _sut.SetStoryAsync(Code, "alice", "x")).Status.Should().Be(SessionActionStatus.SessionClosed);
        (await _sut.SetDeckAsync(Code, "alice", DeckType.TShirt, null)).Status.Should().Be(SessionActionStatus.SessionClosed);
        (await _sut.ChangeRoleAsync(Code, "bob", "bob", ParticipantRole.Observer)).Status.Should().Be(SessionActionStatus.SessionClosed);
    }

    [Fact]
    public async Task A_closed_session_rejects_new_joiners_but_lets_existing_ones_reconnect()
    {
        await SeedAsync();
        await _sut.CloseSessionAsync(Code, "alice");

        var newcomer = await _sut.JoinAsync(new JoinSessionRequest(Code, "carol", "Carol", ParticipantRole.Voter, null));
        newcomer.Status.Should().Be(JoinStatus.SessionClosed);

        var reconnect = await _sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter, null));
        reconnect.Status.Should().Be(JoinStatus.Ok); // already a participant — may still view
    }

    [Fact]
    public async Task Close_is_idempotent_and_stops_a_running_timer()
    {
        await SeedAsync();
        await _sut.StartTimerAsync(Code, "alice", 60);

        var first = await _sut.CloseSessionAsync(Code, "alice");
        first.Session!.TimerDeadline.Should().BeNull(); // running timer frozen on close

        var again = await _sut.CloseSessionAsync(Code, "alice");
        again.Status.Should().Be(SessionActionStatus.Ok);
        again.Session!.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task A_non_organiser_cannot_close()
    {
        await SeedAsync();

        (await _sut.CloseSessionAsync(Code, "bob")).Status.Should().Be(SessionActionStatus.NotOrganiser);
    }

    // --- Delete (soft) ---

    [Fact]
    public async Task Delete_hides_the_session_from_every_read()
    {
        await SeedAsync();

        var result = await _sut.DeleteSessionAsync(Code, "alice");

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session.Should().BeNull(); // gone — nothing to broadcast
        (await _sut.GetByShortCodeAsync(Code)).Should().BeNull(); // invisible
    }

    [Fact]
    public async Task Actions_on_a_deleted_session_report_not_found()
    {
        await SeedAsync();
        await _sut.DeleteSessionAsync(Code, "alice");

        (await _sut.RevealAsync(Code, "alice")).Status.Should().Be(SessionActionStatus.SessionNotFound);
        (await _sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter, null))).Status
            .Should().Be(JoinStatus.SessionNotFound);
    }

    [Fact]
    public async Task A_closed_session_can_still_be_deleted()
    {
        await SeedAsync();
        await _sut.CloseSessionAsync(Code, "alice");

        var result = await _sut.DeleteSessionAsync(Code, "alice");

        result.Status.Should().Be(SessionActionStatus.Ok);
        (await _sut.GetByShortCodeAsync(Code)).Should().BeNull();
    }

    [Fact]
    public async Task A_non_organiser_cannot_delete()
    {
        await SeedAsync();

        (await _sut.DeleteSessionAsync(Code, "bob")).Status.Should().Be(SessionActionStatus.NotOrganiser);
    }
}
