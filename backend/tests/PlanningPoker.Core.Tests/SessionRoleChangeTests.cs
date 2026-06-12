using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

/// <summary>Behaviour of organiser-controlled mid-session role changes (#21).</summary>
public class SessionRoleChangeTests
{
    private const string Code = "blue-fox-42";
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public SessionRoleChangeTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
    }

    /// <summary>Organiser = alice (Observer); bob joins as a Voter.</summary>
    private async Task SeedAsync()
    {
        await _sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, "alice", "Alice", true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter, null));
    }

    private ParticipantInfo Find(SessionSnapshot s, string userId) => s.Participants.Single(p => p.UserId == userId);

    [Fact]
    public async Task Role_changes_are_allowed_by_default()
    {
        await SeedAsync();
        (await _sut.GetByShortCodeAsync(Code))!.AllowRoleChange.Should().BeTrue();
    }

    [Fact]
    public async Task A_participant_can_switch_their_own_role_when_allowed()
    {
        await SeedAsync();

        var result = await _sut.ChangeRoleAsync(Code, "bob", "bob", ParticipantRole.Observer);

        result.Status.Should().Be(SessionActionStatus.Ok);
        Find(result.Session!, "bob").Role.Should().Be(ParticipantRole.Observer);
    }

    [Fact]
    public async Task Switching_to_observer_clears_the_held_vote()
    {
        await SeedAsync();
        await _sut.CastVoteAsync(Code, "bob", "5");

        var result = await _sut.ChangeRoleAsync(Code, "bob", "bob", ParticipantRole.Observer);

        Find(result.Session!, "bob").HasVoted.Should().BeFalse();
    }

    [Fact]
    public async Task A_non_organiser_cannot_switch_their_own_role_when_disabled()
    {
        await SeedAsync();
        await _sut.SetAllowRoleChangeAsync(Code, "alice", false);

        var result = await _sut.ChangeRoleAsync(Code, "bob", "bob", ParticipantRole.Observer);

        result.Status.Should().Be(SessionActionStatus.RoleChangeDisabled);
        Find((await _sut.GetByShortCodeAsync(Code))!, "bob").Role.Should().Be(ParticipantRole.Voter);
    }

    [Fact]
    public async Task A_non_organiser_cannot_change_someone_elses_role()
    {
        await SeedAsync();
        await _sut.JoinAsync(new JoinSessionRequest(Code, "carol", "Carol", ParticipantRole.Voter, null));

        var result = await _sut.ChangeRoleAsync(Code, "bob", "carol", ParticipantRole.Observer);

        result.Status.Should().Be(SessionActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task The_organiser_can_change_another_participants_role_even_when_self_change_is_disabled()
    {
        await SeedAsync();
        await _sut.SetAllowRoleChangeAsync(Code, "alice", false);

        var result = await _sut.ChangeRoleAsync(Code, "alice", "bob", ParticipantRole.Observer);

        result.Status.Should().Be(SessionActionStatus.Ok);
        Find(result.Session!, "bob").Role.Should().Be(ParticipantRole.Observer);
    }

    [Fact]
    public async Task The_organiser_can_change_their_own_role_even_when_self_change_is_disabled()
    {
        await SeedAsync();
        await _sut.SetAllowRoleChangeAsync(Code, "alice", false);

        var result = await _sut.ChangeRoleAsync(Code, "alice", "alice", ParticipantRole.Voter);

        result.Status.Should().Be(SessionActionStatus.Ok);
        Find(result.Session!, "alice").Role.Should().Be(ParticipantRole.Voter);
    }

    [Fact]
    public async Task An_unknown_target_is_rejected()
    {
        await SeedAsync();

        var result = await _sut.ChangeRoleAsync(Code, "alice", "ghost", ParticipantRole.Observer);

        result.Status.Should().Be(SessionActionStatus.TargetNotFound);
    }

    [Fact]
    public async Task The_last_unvoted_voter_becoming_an_observer_completes_an_auto_reveal_round()
    {
        // alice organiser (Observer), bob + carol voters; auto-reveal on; only carol has voted.
        await SeedAsync();
        await _sut.JoinAsync(new JoinSessionRequest(Code, "carol", "Carol", ParticipantRole.Voter, null));
        await _sut.SetAutoRevealAsync(Code, "alice", true);
        await _sut.CastVoteAsync(Code, "carol", "5");
        (await _sut.GetByShortCodeAsync(Code))!.State.Should().Be(SessionState.Voting); // bob still outstanding

        // bob bows out → every remaining connected voter (carol) has voted → auto-reveal fires.
        var result = await _sut.ChangeRoleAsync(Code, "bob", "bob", ParticipantRole.Observer);

        result.Session!.State.Should().Be(SessionState.Revealed);
    }

    [Fact]
    public async Task A_non_participant_cannot_change_roles()
    {
        await SeedAsync();

        var result = await _sut.ChangeRoleAsync(Code, "stranger", "bob", ParticipantRole.Observer);

        result.Status.Should().Be(SessionActionStatus.NotParticipant);
    }

    [Fact]
    public async Task A_non_organiser_cannot_toggle_the_allow_flag()
    {
        await SeedAsync();

        var result = await _sut.SetAllowRoleChangeAsync(Code, "bob", false);

        result.Status.Should().Be(SessionActionStatus.NotOrganiser);
    }
}
