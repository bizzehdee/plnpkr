namespace PlanningPoker.Core;

/// <summary>
/// Generates short, URL-friendly invite codes (e.g. "blue-fox-42"). Injected so tests can supply
/// a deterministic generator. See #1.
/// </summary>
public interface IShortCodeGenerator
{
    string Generate();
}

/// <summary>Default generator: adjective-noun-number, readable and easy to share verbally.</summary>
public sealed class ShortCodeGenerator : IShortCodeGenerator
{
    private static readonly string[] Adjectives =
    {
        "blue", "red", "green", "gold", "swift", "calm", "bold", "bright",
        "lucky", "happy", "clever", "brave", "quiet", "eager", "kind", "wise",
    };

    private static readonly string[] Nouns =
    {
        "fox", "owl", "bear", "wolf", "hawk", "lion", "lynx", "crane",
        "otter", "raven", "tiger", "moose", "heron", "puma", "koala", "ibex",
    };

    private readonly Random _random;

    public ShortCodeGenerator(Random? random = null) => _random = random ?? Random.Shared;

    public string Generate()
    {
        var adjective = Adjectives[_random.Next(Adjectives.Length)];
        var noun = Nouns[_random.Next(Nouns.Length)];
        var number = _random.Next(10, 100);
        return $"{adjective}-{noun}-{number}";
    }
}
