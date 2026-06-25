using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

/// <summary>Velocity/throughput analytics: round recording on reset-from-revealed + the summary (#11).</summary>
public class AnalyticsTests
{
    private const string Code = "blue-fox-42";
    private const string Organiser = "alice";
    private const string Bob = "bob";
    private const string Carol = "carol";

    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public AnalyticsTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private async Task SeedAsync()
    {
        await _sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, Organiser, "Alice", true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Carol, "Carol", ParticipantRole.Voter));
    }

    private async Task EstimateAsync(string story, string bobVote, string carolVote)
    {
        _clock.Advance(TimeSpan.FromMinutes(1)); // distinct RecordedAt so ordering is unambiguous
        await _sut.SetStoryAsync(Code, Organiser, story);
        await _sut.CastVoteAsync(Code, Bob, bobVote);
        await _sut.CastVoteAsync(Code, Carol, carolVote);
        await _sut.RevealAsync(Code, Organiser);
        await _sut.ResetRoundAsync(Code, Organiser); // completes + records the round
    }

    [Fact]
    public async Task A_revealed_round_is_recorded_on_reset_with_consensus_and_final_estimate()
    {
        await SeedAsync();

        await EstimateAsync("Story A", "5", "5"); // unanimous

        var analytics = (await _sut.GetAnalyticsAsync(Code))!;
        analytics.RoundsCompleted.Should().Be(1);
        analytics.ConsensusRounds.Should().Be(1);
        analytics.ConsensusRate.Should().Be(1.0);
        var round = analytics.Rounds.Single();
        round.Story.Should().Be("Story A");
        round.Consensus.Should().BeTrue();
        round.FinalEstimate.Should().Be("5");
    }

    [Fact]
    public async Task Resetting_a_voting_round_records_nothing()
    {
        await SeedAsync();
        await _sut.CastVoteAsync(Code, Bob, "5"); // not revealed

        await _sut.ResetRoundAsync(Code, Organiser);

        (await _sut.GetAnalyticsAsync(Code))!.RoundsCompleted.Should().Be(0);
    }

    [Fact]
    public async Task Multiple_rounds_accumulate_with_a_correct_consensus_rate_newest_first()
    {
        await SeedAsync();

        await EstimateAsync("Story A", "5", "5"); // consensus
        await EstimateAsync("Story B", "3", "13"); // no consensus

        var analytics = (await _sut.GetAnalyticsAsync(Code))!;
        analytics.RoundsCompleted.Should().Be(2);
        analytics.ConsensusRounds.Should().Be(1);
        analytics.ConsensusRate.Should().Be(0.5);
        analytics.Rounds[0].Story.Should().Be("Story B", "rounds are newest-first");
        analytics.Rounds[1].Story.Should().Be("Story A");
    }

    [Fact]
    public async Task Analytics_for_an_unknown_session_is_null()
    {
        (await _sut.GetAnalyticsAsync("no-such-code")).Should().BeNull();
    }
}
