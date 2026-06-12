using PlanningPoker.Core.Models;

namespace PlanningPoker.Core;

/// <summary>
/// Resolves a <see cref="DeckType"/> (or a custom list) to the concrete card values shown to users.
/// Pure and deterministic so it can be exhaustively unit-tested. See #32.
/// </summary>
public static class DeckCatalog
{
    /// <summary>Unsure.</summary>
    public const string Unsure = "?";

    /// <summary>Need a break.</summary>
    public const string Coffee = "☕";

    /// <summary>Non-numeric cards appended to every deck. Never counted in stats.</summary>
    public static readonly IReadOnlyList<string> NonNumericCards = new[] { Unsure, Coffee };

    private static readonly IReadOnlyList<string> Sequential =
        new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };

    private static readonly IReadOnlyList<string> Fibonacci =
        new[] { "0", "1", "2", "3", "5", "8", "13", "21", "34", "55", "89" };

    private static readonly IReadOnlyList<string> ModifiedFibonacci =
        new[] { "0", "½", "1", "2", "3", "5", "8", "13", "20", "40", "100" };

    private static readonly IReadOnlyList<string> TShirt =
        new[] { "XS", "S", "M", "L", "XL", "XXL" };

    private static readonly IReadOnlyList<string> PowersOfTwo =
        new[] { "0", "1", "2", "4", "8", "16", "32", "64" };

    /// <summary>
    /// Returns the full card set for a deck, with <see cref="Unsure"/> and <see cref="Coffee"/>
    /// appended. For <see cref="DeckType.Custom"/>, <paramref name="customCards"/> is a
    /// comma-separated list of values.
    /// </summary>
    /// <exception cref="ArgumentException">Custom deck with no usable values.</exception>
    public static IReadOnlyList<string> GetCards(DeckType deckType, string? customCards = null)
    {
        var baseCards = deckType switch
        {
            DeckType.Sequential => Sequential,
            DeckType.Fibonacci => Fibonacci,
            DeckType.ModifiedFibonacci => ModifiedFibonacci,
            DeckType.TShirt => TShirt,
            DeckType.PowersOfTwo => PowersOfTwo,
            DeckType.Custom => ParseCustom(customCards),
            _ => throw new ArgumentOutOfRangeException(nameof(deckType), deckType, "Unknown deck type."),
        };

        return baseCards.Concat(NonNumericCards).ToArray();
    }

    private static IReadOnlyList<string> ParseCustom(string? customCards)
    {
        var values = (customCards ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (values.Length == 0)
        {
            throw new ArgumentException("A custom deck must contain at least one card value.", nameof(customCards));
        }

        return values;
    }
}
