using FluentAssertions;
using TeamTools.Core;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>Timed discussion phase (#9): Revealed → Discussion → Voting, with timer auto-advance.</summary>
public class DiscussionTests
{
    private const string Code = "blue-fox-42";
    private const string Organiser = "alice";
    private const string Bob = "bob";

    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;
    private readonly SessionMaintenanceService _maintenance;

    public DiscussionTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
        _maintenance = new SessionMaintenanceService(_store, _clock);
    }

    private async Task SeedRevealedAsync()
    {
        await _sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, Organiser, "Alice", true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
        await _sut.CastVoteAsync(Code, Bob, "5");
        await _sut.RevealAsync(Code, Organiser);
    }

    [Fact]
    public async Task Organiser_starts_a_discussion_phase_keeping_votes_on_screen()
    {
        await SeedRevealedAsync();

        var result = await _sut.StartDiscussionAsync(Code, Organiser);

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.State.Should().Be(SessionState.Discussion);
        result.Session.Participants.Single(p => p.UserId == Bob).HasVoted.Should().BeTrue("votes are kept during discussion");
    }

    [Fact]
    public async Task Starting_a_discussion_with_seconds_starts_a_countdown()
    {
        await SeedRevealedAsync();

        var result = await _sut.StartDiscussionAsync(Code, Organiser, 60);

        result.Session!.TimerDeadline.Should().Be(_clock.UtcNow.AddSeconds(60));
    }

    [Fact]
    public async Task A_non_organiser_cannot_start_a_discussion()
    {
        await SeedRevealedAsync();

        var result = await _sut.StartDiscussionAsync(Code, Bob);

        result.Status.Should().Be(SessionActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task Ending_a_discussion_clears_votes_and_returns_to_voting()
    {
        await SeedRevealedAsync();
        await _sut.StartDiscussionAsync(Code, Organiser);

        var result = await _sut.EndDiscussionAsync(Code, Organiser);

        result.Session!.State.Should().Be(SessionState.Voting);
        result.Session.Participants.Should().OnlyContain(p => !p.HasVoted && p.Vote == null);
    }

    [Fact]
    public async Task An_expired_discussion_timer_auto_advances_to_a_fresh_re_vote()
    {
        await SeedRevealedAsync();
        await _sut.StartDiscussionAsync(Code, Organiser, 30);

        _clock.Advance(TimeSpan.FromSeconds(31));
        var expired = await _maintenance.ExpireDueRoundTimersAsync();

        expired.Should().ContainSingle();
        var snapshot = expired[0];
        snapshot.State.Should().Be(SessionState.Voting);
        snapshot.Participants.Should().OnlyContain(p => !p.HasVoted);
        snapshot.TimerDeadline.Should().BeNull();
    }
}
