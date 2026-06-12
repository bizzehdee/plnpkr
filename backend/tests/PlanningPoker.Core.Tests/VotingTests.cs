using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class VotingTests
{
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public VotingTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator("blue-fox-42"), _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Organiser = "alice";
    private const string Bob = "bob";
    private const string Carol = "carol";

    /// <summary>Alice creates+organises (Observer); Bob and Carol join as voters.</summary>
    private async Task SeedAsync(bool organise = true)
    {
        await _sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, Organiser, "Alice", organise));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Carol, "Carol", ParticipantRole.Voter));
    }

    private async Task<SessionSnapshot> SnapshotAsync() => (await _sut.GetByShortCodeAsync(Code))!;

    private ParticipantInfo Participant(SessionSnapshot s, string userId) =>
        s.Participants.Single(p => p.UserId == userId);

    // --- Casting -----------------------------------------------------------

    [Fact]
    public async Task Voter_can_cast_and_has_voted_is_visible_but_value_is_hidden_while_voting()
    {
        await SeedAsync();

        var result = await _sut.CastVoteAsync(Code, Bob, "5");

        result.Status.Should().Be(SessionActionStatus.Ok);
        var bob = Participant(result.Session!, Bob);
        bob.HasVoted.Should().BeTrue();
        bob.Vote.Should().BeNull("vote values are hidden until revealed");
    }

    [Fact]
    public async Task Observer_cannot_vote()
    {
        await SeedAsync();

        var result = await _sut.CastVoteAsync(Code, Organiser, "5");

        result.Status.Should().Be(SessionActionStatus.ObserverCannotVote);
    }

    [Fact]
    public async Task Card_outside_the_deck_is_rejected()
    {
        await SeedAsync();

        var result = await _sut.CastVoteAsync(Code, Bob, "999");

        result.Status.Should().Be(SessionActionStatus.InvalidCard);
    }

    [Fact]
    public async Task Non_participant_cannot_vote()
    {
        await SeedAsync();

        var result = await _sut.CastVoteAsync(Code, "stranger", "5");

        result.Status.Should().Be(SessionActionStatus.NotParticipant);
    }

    // --- Reveal & permissions ---------------------------------------------

    [Fact]
    public async Task Reveal_exposes_vote_values_and_stats()
    {
        await SeedAsync();
        await _sut.CastVoteAsync(Code, Bob, "5");
        await _sut.CastVoteAsync(Code, Carol, "8");

        var result = await _sut.RevealAsync(Code, Organiser);

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.State.Should().Be(SessionState.Revealed);
        Participant(result.Session, Bob).Vote.Should().Be("5");
        result.Session.Stats!.Average.Should().Be(6.5);
    }

    [Fact]
    public async Task Non_organiser_cannot_reveal()
    {
        await SeedAsync();

        var result = await _sut.RevealAsync(Code, Bob);

        result.Status.Should().Be(SessionActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task When_session_has_no_organiser_any_participant_can_reveal()
    {
        await SeedAsync(organise: false); // Alice created without organising -> no organiser

        var result = await _sut.RevealAsync(Code, Bob);

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.State.Should().Be(SessionState.Revealed);
    }

    // --- Auto-reveal -------------------------------------------------------

    [Fact]
    public async Task Auto_reveal_fires_when_the_last_voter_votes()
    {
        await SeedAsync();
        await _sut.SetAutoRevealAsync(Code, Organiser, true);

        await _sut.CastVoteAsync(Code, Bob, "5");
        var afterFirst = await SnapshotAsync();
        afterFirst.State.Should().Be(SessionState.Voting, "Carol has not voted yet");

        var result = await _sut.CastVoteAsync(Code, Carol, "8");

        result.Session!.State.Should().Be(SessionState.Revealed);
    }

    [Fact]
    public async Task Enabling_auto_reveal_when_all_votes_are_in_reveals_immediately()
    {
        await SeedAsync();
        await _sut.CastVoteAsync(Code, Bob, "5");
        await _sut.CastVoteAsync(Code, Carol, "8");

        var result = await _sut.SetAutoRevealAsync(Code, Organiser, true);

        result.Session!.State.Should().Be(SessionState.Revealed);
    }

    [Fact]
    public async Task Auto_reveal_does_not_fire_with_no_voters()
    {
        // Only the organiser-observer is present; enabling auto-reveal must not reveal.
        await _sut.CreateAsync(new CreateSessionRequest("Solo", DeckType.Fibonacci, null, Organiser, "Alice", true));

        var result = await _sut.SetAutoRevealAsync(Code, Organiser, true);

        result.Session!.State.Should().Be(SessionState.Voting);
    }

    [Fact]
    public async Task Reveal_flags_the_diverging_participant_as_an_outlier()
    {
        await SeedAsync(); // Alice organiser-observer, Bob + Carol voters
        await _sut.JoinAsync(new JoinSessionRequest(Code, "dave", "Dave", ParticipantRole.Voter));
        await _sut.CastVoteAsync(Code, Bob, "3");
        await _sut.CastVoteAsync(Code, Carol, "5");
        await _sut.CastVoteAsync(Code, "dave", "13");

        var result = await _sut.RevealAsync(Code, Organiser);

        Participant(result.Session!, "dave").IsOutlier.Should().BeTrue();
        Participant(result.Session!, Bob).IsOutlier.Should().BeFalse();
        result.Session!.Stats!.OutlierValues.Should().Contain("13");
    }

    [Fact]
    public async Task Outlier_clears_when_the_group_converges_after_reveal()
    {
        await SeedAsync();
        await _sut.JoinAsync(new JoinSessionRequest(Code, "dave", "Dave", ParticipantRole.Voter));
        await _sut.CastVoteAsync(Code, Bob, "3");
        await _sut.CastVoteAsync(Code, Carol, "5");
        await _sut.CastVoteAsync(Code, "dave", "13");
        await _sut.RevealAsync(Code, Organiser);

        // The group talks it through and converges on 5 — outliers recompute and clear.
        await _sut.CastVoteAsync(Code, Bob, "5");
        var result = await _sut.CastVoteAsync(Code, "dave", "5");

        result.Session!.Stats!.OutlierValues.Should().BeEmpty();
        result.Session.Participants.Should().OnlyContain(p => !p.IsOutlier);
    }

    // --- Editing after reveal ---------------------------------------------

    [Fact]
    public async Task Changing_a_vote_after_reveal_sets_the_edited_flag_and_recomputes_stats()
    {
        await SeedAsync();
        await _sut.CastVoteAsync(Code, Bob, "5");
        await _sut.CastVoteAsync(Code, Carol, "5");
        await _sut.RevealAsync(Code, Organiser);

        var result = await _sut.CastVoteAsync(Code, Carol, "13");

        var carol = Participant(result.Session!, Carol);
        carol.Vote.Should().Be("13");
        carol.ChangedAfterReveal.Should().BeTrue();
        result.Session!.Stats!.Average.Should().Be(9); // (5 + 13) / 2
    }

    [Fact]
    public async Task Casting_before_reveal_does_not_set_the_edited_flag()
    {
        await SeedAsync();

        await _sut.CastVoteAsync(Code, Bob, "3");
        var result = await _sut.CastVoteAsync(Code, Bob, "5"); // changed while still voting

        Participant(result.Session!, Bob).ChangedAfterReveal.Should().BeFalse();
    }

    // --- Resets ------------------------------------------------------------

    [Fact]
    public async Task Reset_vote_clears_one_participant_and_its_edited_flag()
    {
        await SeedAsync();
        await _sut.CastVoteAsync(Code, Bob, "5");
        await _sut.CastVoteAsync(Code, Carol, "8");
        await _sut.RevealAsync(Code, Organiser);
        await _sut.CastVoteAsync(Code, Bob, "13"); // Bob edited after reveal

        var result = await _sut.ResetVoteAsync(Code, Organiser, Bob);

        var bob = Participant(result.Session!, Bob);
        bob.HasVoted.Should().BeFalse();
        bob.Vote.Should().BeNull();
        bob.ChangedAfterReveal.Should().BeFalse();
        // Carol's vote remains, so stats reflect only her.
        result.Session!.Stats!.Average.Should().Be(8);
    }

    [Fact]
    public async Task Reset_vote_for_unknown_target_returns_target_not_found()
    {
        await SeedAsync();

        var result = await _sut.ResetVoteAsync(Code, Organiser, "ghost");

        result.Status.Should().Be(SessionActionStatus.TargetNotFound);
    }

    [Fact]
    public async Task Reset_round_clears_all_votes_and_returns_to_voting()
    {
        await SeedAsync();
        await _sut.CastVoteAsync(Code, Bob, "5");
        await _sut.CastVoteAsync(Code, Carol, "8");
        await _sut.RevealAsync(Code, Organiser);

        var result = await _sut.ResetRoundAsync(Code, Organiser);

        result.Session!.State.Should().Be(SessionState.Voting);
        result.Session.Participants.Should().OnlyContain(p => !p.HasVoted && p.Vote == null);
    }

    [Fact]
    public async Task Non_organiser_cannot_reset_round()
    {
        await SeedAsync();

        var result = await _sut.ResetRoundAsync(Code, Bob);

        result.Status.Should().Be(SessionActionStatus.NotOrganiser);
    }

    // --- Story -------------------------------------------------------------

    [Fact]
    public async Task Organiser_can_set_the_current_story()
    {
        await SeedAsync();

        var result = await _sut.SetStoryAsync(Code, Organiser, "  PROJ-123 login  ");

        result.Session!.CurrentStory.Should().Be("PROJ-123 login");
    }

    [Fact]
    public async Task Non_organiser_cannot_set_the_story()
    {
        await SeedAsync();

        var result = await _sut.SetStoryAsync(Code, Bob, "sneaky");

        result.Status.Should().Be(SessionActionStatus.NotOrganiser);
    }
}
