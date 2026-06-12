using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Models;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class DeckCatalogTests
{
    [Theory]
    [InlineData(DeckType.Sequential, "0", "10")]
    [InlineData(DeckType.Fibonacci, "0", "89")]
    [InlineData(DeckType.ModifiedFibonacci, "0", "100")]
    [InlineData(DeckType.TShirt, "XS", "XXL")]
    [InlineData(DeckType.PowersOfTwo, "0", "64")]
    public void GetCards_returns_expected_first_and_last_numeric_card(DeckType deck, string first, string lastValue)
    {
        var cards = DeckCatalog.GetCards(deck);

        cards[0].Should().Be(first);
        // last value card sits just before the two appended non-numeric cards
        cards[^3].Should().Be(lastValue);
    }

    [Theory]
    [InlineData(DeckType.Sequential)]
    [InlineData(DeckType.Fibonacci)]
    [InlineData(DeckType.ModifiedFibonacci)]
    [InlineData(DeckType.TShirt)]
    [InlineData(DeckType.PowersOfTwo)]
    public void Every_deck_appends_unsure_and_coffee(DeckType deck)
    {
        var cards = DeckCatalog.GetCards(deck);

        cards.Should().ContainInOrder("?", "☕");
        cards[^2].Should().Be("?");
        cards[^1].Should().Be("☕");
    }

    [Fact]
    public void Custom_deck_uses_supplied_values_trimmed_and_deduplicated()
    {
        var cards = DeckCatalog.GetCards(DeckType.Custom, " 1 , 2 , 2 , 3 ");

        cards.Should().Equal("1", "2", "3", "?", "☕");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",, ,")]
    public void Custom_deck_with_no_values_throws(string? custom)
    {
        var act = () => DeckCatalog.GetCards(DeckType.Custom, custom);

        act.Should().Throw<ArgumentException>();
    }
}
