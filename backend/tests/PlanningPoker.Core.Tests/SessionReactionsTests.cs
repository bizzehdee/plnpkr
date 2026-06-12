using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

/// <summary>Behaviour of the organiser-controlled emoji-reactions toggle (#17).</summary>
public class SessionReactionsTests
{
    private const string Code = "blue-fox-42";
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public SessionReactionsTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private Task<CreateSessionResult> CreateAsync(bool enableReactions) =>
        _sut.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, null, enableReactions));

    [Fact]
    public async Task Reactions_are_enabled_by_default_on_creation()
    {
        var result = await CreateAsync(enableReactions: true);

        result.Session!.ReactionsEnabled.Should().BeTrue();
        (await _sut.AreReactionsEnabledAsync(Code)).Should().BeTrue();
    }

    [Fact]
    public async Task The_organiser_can_disable_reactions_at_creation()
    {
        var result = await CreateAsync(enableReactions: false);

        result.Session!.ReactionsEnabled.Should().BeFalse();
        (await _sut.AreReactionsEnabledAsync(Code)).Should().BeFalse();
    }

    [Fact]
    public async Task The_organiser_can_re_enable_reactions_mid_session()
    {
        await CreateAsync(enableReactions: false);

        var result = await _sut.SetReactionsEnabledAsync(Code, "alice", true);

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.ReactionsEnabled.Should().BeTrue();
        (await _sut.AreReactionsEnabledAsync(Code)).Should().BeTrue();
    }

    [Fact]
    public async Task The_organiser_can_disable_reactions_again_mid_session()
    {
        await CreateAsync(enableReactions: true);

        var result = await _sut.SetReactionsEnabledAsync(Code, "alice", false);

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.ReactionsEnabled.Should().BeFalse();
        (await _sut.AreReactionsEnabledAsync(Code)).Should().BeFalse();
    }

    [Fact]
    public async Task A_non_organiser_cannot_toggle_reactions()
    {
        await CreateAsync(enableReactions: true);
        await _sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter, null));

        var result = await _sut.SetReactionsEnabledAsync(Code, "bob", false);

        result.Status.Should().Be(SessionActionStatus.NotOrganiser);
        (await _sut.AreReactionsEnabledAsync(Code)).Should().BeTrue();
    }

    [Fact]
    public async Task AreReactionsEnabled_is_false_for_an_unknown_session()
    {
        (await _sut.AreReactionsEnabledAsync("no-such-session")).Should().BeFalse();
    }
}
