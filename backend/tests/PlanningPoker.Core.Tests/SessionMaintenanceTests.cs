using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class SessionMaintenanceTests
{
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sessions;
    private readonly SessionMaintenanceService _sut;

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(60);

    private const string Code = "blue-fox-42";

    public SessionMaintenanceTests()
    {
        _sessions = new SessionService(_store, new StubShortCodeGenerator(Code, "red-owl-99"), _clock);
        _sut = new SessionMaintenanceService(_store, _clock);
    }

    private async Task SeedAsync(string code = Code, string creator = "alice")
    {
        var gen = new StubShortCodeGenerator(code);
        var svc = new SessionService(_store, gen, _clock);
        await svc.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, creator, "Alice", false));
    }

    [Fact]
    public async Task Disconnected_participant_past_grace_is_removed()
    {
        await SeedAsync();
        await _sessions.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter));
        await _sessions.MarkDisconnectedAsync(Code, "bob");

        _clock.Advance(Grace + TimeSpan.FromSeconds(1));
        var report = await _sut.PurgeAsync(Grace, Idle);

        var session = await _store.FindByShortCodeAsync(Code);
        session!.Participants.Select(p => p.UserId).Should().NotContain("bob");
        report.UpdatedSessions.Should().ContainSingle(s => s.ShortCode == Code);
    }

    [Fact]
    public async Task Recently_disconnected_participant_within_grace_is_kept()
    {
        await SeedAsync();
        await _sessions.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter));
        await _sessions.MarkDisconnectedAsync(Code, "bob");

        _clock.Advance(TimeSpan.FromSeconds(30)); // still inside the grace window
        await _sut.PurgeAsync(Grace, Idle);

        var session = await _store.FindByShortCodeAsync(Code);
        session!.Participants.Select(p => p.UserId).Should().Contain("bob");
    }

    [Fact]
    public async Task Session_left_empty_after_eviction_is_deleted()
    {
        await SeedAsync(); // only Alice (a connected observer-less voter)
        await _sessions.MarkDisconnectedAsync(Code, "alice");

        _clock.Advance(Grace + TimeSpan.FromSeconds(1));
        var report = await _sut.PurgeAsync(Grace, Idle);

        (await _store.FindByShortCodeAsync(Code)).Should().BeNull();
        report.RemovedShortCodes.Should().Contain(Code);
    }

    [Fact]
    public async Task Idle_session_past_the_idle_window_is_deleted_even_with_connected_members()
    {
        await SeedAsync();

        _clock.Advance(Idle + TimeSpan.FromMinutes(1));
        var report = await _sut.PurgeAsync(Grace, Idle);

        (await _store.FindByShortCodeAsync(Code)).Should().BeNull();
        report.RemovedShortCodes.Should().Contain(Code);
    }

    [Fact]
    public async Task Active_session_with_connected_members_is_left_untouched()
    {
        await SeedAsync();

        _clock.Advance(TimeSpan.FromMinutes(5));
        var report = await _sut.PurgeAsync(Grace, Idle);

        (await _store.FindByShortCodeAsync(Code)).Should().NotBeNull();
        report.RemovedShortCodes.Should().BeEmpty();
        report.UpdatedSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Evicting_an_away_organiser_clears_the_organiser()
    {
        // Organiser session: Alice organiser-observer, Bob a connected voter (keeps session alive).
        var svc = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
        await svc.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, "alice", "Alice", true));
        await svc.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter));
        await svc.MarkDisconnectedAsync(Code, "alice");

        _clock.Advance(Grace + TimeSpan.FromSeconds(1));
        await _sut.PurgeAsync(Grace, Idle);

        var session = await _store.FindByShortCodeAsync(Code);
        session.Should().NotBeNull();
        session!.OrganiserUserId.Should().BeNull();
        session.Participants.Select(p => p.UserId).Should().Contain("bob").And.NotContain("alice");
    }
}
