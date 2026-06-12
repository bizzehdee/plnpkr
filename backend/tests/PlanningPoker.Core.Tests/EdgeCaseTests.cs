using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

/// <summary>Covers the less-trodden branches: invalid enums, the short-code collision fallback,
/// empty-story clearing, and control attempts by non-participants.</summary>
public class EdgeCaseTests
{
    private sealed class ConstantShortCodeGenerator : IShortCodeGenerator
    {
        private readonly string _value;
        public ConstantShortCodeGenerator(string value) => _value = value;
        public string Generate() => _value;
    }

    [Fact]
    public void DeckCatalog_throws_for_an_unknown_deck_type()
    {
        var act = () => DeckCatalog.GetCards((DeckType)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Short_code_generation_falls_back_when_every_attempt_collides()
    {
        var store = new FakeSessionStore();
        var clock = new TestClock();
        // A generator that always returns the same code forces a collision on every attempt.
        var sut = new SessionService(store, new ConstantShortCodeGenerator("dup"), clock);

        var first = await sut.CreateAsync(new CreateSessionRequest("A", DeckType.Fibonacci, null, "u1", "Al", false));
        var second = await sut.CreateAsync(new CreateSessionRequest("B", DeckType.Fibonacci, null, "u2", "Bo", false));

        first.Session!.ShortCode.Should().Be("dup");
        second.Session!.ShortCode.Should().StartWith("dup-").And.NotBe("dup"); // guid-suffixed fallback
    }

    [Fact]
    public async Task Setting_a_blank_story_clears_it()
    {
        var store = new FakeSessionStore();
        var sut = new SessionService(store, new StubShortCodeGenerator("blue-fox-42"), new TestClock());
        await sut.CreateAsync(new CreateSessionRequest("S", DeckType.Fibonacci, null, "alice", "Alice", true));
        await sut.SetStoryAsync("blue-fox-42", "alice", "something");

        var result = await sut.SetStoryAsync("blue-fox-42", "alice", "   ");

        result.Session!.CurrentStory.Should().BeNull();
    }

    [Fact]
    public async Task Control_action_by_a_non_participant_is_rejected()
    {
        var store = new FakeSessionStore();
        var sut = new SessionService(store, new StubShortCodeGenerator("blue-fox-42"), new TestClock());
        await sut.CreateAsync(new CreateSessionRequest("S", DeckType.Fibonacci, null, "alice", "Alice", true));

        var result = await sut.RevealAsync("blue-fox-42", "stranger");

        result.Status.Should().Be(SessionActionStatus.NotParticipant);
    }

    [Fact]
    public async Task Mark_disconnected_for_unknown_session_or_user_is_handled()
    {
        var store = new FakeSessionStore();
        var sut = new SessionService(store, new StubShortCodeGenerator("blue-fox-42"), new TestClock());
        await sut.CreateAsync(new CreateSessionRequest("S", DeckType.Fibonacci, null, "alice", "Alice", true));

        (await sut.MarkDisconnectedAsync("missing-code-1", "alice")).Status.Should().Be(SessionActionStatus.SessionNotFound);
        (await sut.MarkDisconnectedAsync("blue-fox-42", "ghost")).Status.Should().Be(SessionActionStatus.NotParticipant);
    }

    [Fact]
    public async Task Voting_actions_on_a_missing_session_return_not_found()
    {
        var store = new FakeSessionStore();
        var sut = new SessionService(store, new StubShortCodeGenerator("x"), new TestClock());

        (await sut.CastVoteAsync("missing-1", "u", "5")).Status.Should().Be(SessionActionStatus.SessionNotFound);
        (await sut.SetAutoRevealAsync("missing-1", "u", true)).Status.Should().Be(SessionActionStatus.SessionNotFound);
    }
}
