using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

/// <summary>Behaviour of the organiser-controlled mid-session deck switch (#11).</summary>
public class SessionDeckTests
{
    private const string Code = "blue-fox-42";
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public SessionDeckTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private async Task SeedAsync(DeckType deck = DeckType.Fibonacci, string? custom = null)
    {
        // Non-organising creator so the creator is a voter and can cast a vote we expect to be cleared.
        await _sut.CreateAsync(new CreateSessionRequest("Sprint", deck, custom, "alice", "Alice", false));
    }

    [Fact]
    public async Task Switching_to_another_built_in_deck_updates_the_cards()
    {
        await SeedAsync(DeckType.Fibonacci);

        var result = await _sut.SetDeckAsync(Code, "alice", DeckType.TShirt, null);

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.DeckType.Should().Be(DeckType.TShirt);
        result.Session.Cards.Should().Contain("M").And.Contain("?"); // T-shirt + the appended non-numeric
        result.Session.Cards.Should().NotContain("13"); // a Fibonacci-only card is gone
    }

    [Fact]
    public async Task Switching_to_a_custom_deck_uses_the_supplied_cards()
    {
        await SeedAsync(DeckType.Fibonacci);

        var result = await _sut.SetDeckAsync(Code, "alice", DeckType.Custom, "1, 2, 4, 8");

        result.Session!.DeckType.Should().Be(DeckType.Custom);
        result.Session.Cards.Should().ContainInOrder("1", "2", "4", "8");
    }

    [Fact]
    public async Task Changing_the_deck_clears_votes_and_resets_to_voting()
    {
        await SeedAsync(DeckType.Fibonacci);
        await _sut.CastVoteAsync(Code, "alice", "5");
        await _sut.RevealAsync(Code, "alice");

        var result = await _sut.SetDeckAsync(Code, "alice", DeckType.PowersOfTwo, null);

        result.Session!.State.Should().Be(SessionState.Voting);
        result.Session.Participants.Single(p => p.UserId == "alice").HasVoted.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_custom_deck_is_rejected()
    {
        await SeedAsync(DeckType.Fibonacci);

        var result = await _sut.SetDeckAsync(Code, "alice", DeckType.Custom, "   ");

        result.Status.Should().Be(SessionActionStatus.InvalidDeck);
        // The session keeps its original deck.
        (await _sut.GetByShortCodeAsync(Code))!.DeckType.Should().Be(DeckType.Fibonacci);
    }

    [Fact]
    public async Task A_non_organiser_cannot_change_the_deck()
    {
        // Organiser = alice; bob joins as a voter and must not be able to switch the deck.
        await _sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, "alice", "Alice", true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter, null));

        var result = await _sut.SetDeckAsync(Code, "bob", DeckType.TShirt, null);

        result.Status.Should().Be(SessionActionStatus.NotOrganiser);
    }
}
