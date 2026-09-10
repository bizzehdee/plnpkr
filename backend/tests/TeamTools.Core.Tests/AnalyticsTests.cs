using FluentAssertions;
using TeamTools.Core.Poker;
using TeamTools.Core;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>Velocity/throughput analytics: round recording on reset-from-revealed + the summary (#11).</summary>
public class AnalyticsTests
{
    private const string Code = "blue-fox-42";
    private const string Organiser = "alice";
    private const string Bob = "bob";
    private const string Carol = "carol";

    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly PokerService _sut;

    public AnalyticsTests()
    {
        _sut = TestServices.Poker(_store, new StubShortCodeGenerator(Code), _clock);
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

    /// <summary>The seeded session's analytics — the common case in this file. See #30 for the password.</summary>
    private async Task<SessionAnalytics> AnalyticsAsync(string? password = null) =>
        (await _sut.GetAnalyticsAsync(Code, password)).Analytics!;

    private async Task<string> CsvAsync(string? password = null) =>
        (await _sut.GetAnalyticsCsvAsync(Code, password)).Csv!;

    [Fact]
    public async Task A_revealed_round_is_recorded_on_reset_with_consensus_and_final_estimate()
    {
        await SeedAsync();

        await EstimateAsync("Story A", "5", "5"); // unanimous

        var analytics = await AnalyticsAsync();
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

        (await AnalyticsAsync()).RoundsCompleted.Should().Be(0);
    }

    [Fact]
    public async Task Multiple_rounds_accumulate_with_a_correct_consensus_rate_newest_first()
    {
        await SeedAsync();

        await EstimateAsync("Story A", "5", "5"); // consensus
        await EstimateAsync("Story B", "3", "13"); // no consensus

        var analytics = await AnalyticsAsync();
        analytics.RoundsCompleted.Should().Be(2);
        analytics.ConsensusRounds.Should().Be(1);
        analytics.ConsensusRate.Should().Be(0.5);
        analytics.Rounds[0].Story.Should().Be("Story B", "rounds are newest-first");
        analytics.Rounds[1].Story.Should().Be("Story A");
    }

    [Fact]
    public async Task Analytics_for_an_unknown_session_is_reported_as_not_found()
    {
        var (status, analytics) = await _sut.GetAnalyticsAsync("no-such-code", null);

        status.Should().Be(SessionExportStatus.SessionNotFound);
        analytics.Should().BeNull();
    }

    // --- CSV export (#12) ---

    [Fact]
    public async Task Csv_export_has_a_header_and_one_row_per_round_with_escaping()
    {
        await SeedAsync();
        await _sut.SetStoryAsync(Code, Organiser, "Login, page"); // comma forces quoting
        await _sut.CastVoteAsync(Code, Bob, "5");
        await _sut.CastVoteAsync(Code, Carol, "5");
        await _sut.RevealAsync(Code, Organiser);
        await _sut.ResetRoundAsync(Code, Organiser);

        var csv = await CsvAsync();
        var lines = csv.TrimEnd().Split('\n');

        lines[0].Trim().Should().Be("RecordedAt,Story,FinalEstimate,Average,Consensus,VoteCount,Note");
        lines.Should().HaveCount(2); // header + one round
        lines[1].Should().Contain("\"Login, page\"", "fields with commas are quoted");
        lines[1].Should().Contain(",5,"); // final estimate 5
    }

    [Fact]
    public async Task Csv_export_for_an_unknown_session_is_reported_as_not_found()
    {
        var (status, csv) = await _sut.GetAnalyticsCsvAsync("no-such-code", null);

        status.Should().Be(SessionExportStatus.SessionNotFound);
        csv.Should().BeNull();
    }

    // --- The password guard (#30) ---

    [Fact]
    public async Task A_protected_sessions_history_needs_its_password()
    {
        // The round history is the whole session in one payload — every story, note and estimate —
        // and a short code is only a bearer token. Before #30 this read was guarded by session
        // existence alone.
        await SeedProtectedAsync();

        var missing = await _sut.GetAnalyticsAsync(Code, null);
        var wrong = await _sut.GetAnalyticsAsync(Code, "guess");
        var right = await _sut.GetAnalyticsAsync(Code, "hunter2");

        missing.Status.Should().Be(SessionExportStatus.PasswordRequired);
        missing.Analytics.Should().BeNull();
        wrong.Status.Should().Be(SessionExportStatus.PasswordRequired);
        right.Status.Should().Be(SessionExportStatus.Ok);
        right.Analytics!.RoundsCompleted.Should().Be(1);
    }

    [Fact]
    public async Task The_csv_export_is_under_the_same_guard()
    {
        // Guarding the JSON read and not the CSV would be theatre: the CSV is a strict subset of it.
        await SeedProtectedAsync();

        var missing = await _sut.GetAnalyticsCsvAsync(Code, null);
        var right = await _sut.GetAnalyticsCsvAsync(Code, "hunter2");

        missing.Status.Should().Be(SessionExportStatus.PasswordRequired);
        missing.Csv.Should().BeNull();
        right.Status.Should().Be(SessionExportStatus.Ok);
        right.Csv.Should().Contain("Story A");
    }

    [Fact]
    public async Task A_session_without_a_password_is_readable_without_one()
    {
        // The guard must not turn every unprotected session into a locked one.
        await SeedAsync();
        await EstimateAsync("Story A", "5", "5");

        var (status, analytics) = await _sut.GetAnalyticsAsync(Code, null);

        status.Should().Be(SessionExportStatus.Ok);
        analytics!.RoundsCompleted.Should().Be(1);
    }

    [Fact]
    public async Task A_password_supplied_for_an_unprotected_session_is_simply_ignored()
    {
        await SeedAsync();

        var (status, _) = await _sut.GetAnalyticsAsync(Code, "anything");

        status.Should().Be(SessionExportStatus.Ok);
    }

    [Fact]
    public async Task The_join_landing_read_stays_open()
    {
        // It is how the join page learns a password is needed at all, and it carries nothing but the
        // session name and that fact — so it is deliberately outside the guard.
        await SeedProtectedAsync();

        var landing = await _sut.GetLandingAsync(Code);

        landing!.RequiresPassword.Should().BeTrue();
        landing.Name.Should().Be("Sprint");
    }

    /// <summary>A password-protected session with one completed round in its history.</summary>
    private async Task SeedProtectedAsync()
    {
        await _sut.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, Organiser, "Alice", true, Password: "hunter2"));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter, "hunter2"));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Carol, "Carol", ParticipantRole.Voter, "hunter2"));
        await EstimateAsync("Story A", "5", "5");
    }
}
