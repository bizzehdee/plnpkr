using TeamTools.Core.Models;

namespace TeamTools.Core.Retro;

/// <summary>
/// Resolves a <see cref="RetroTemplate"/> (or a custom list) to the concrete column titles a board
/// starts with. Pure and deterministic, mirroring <c>DeckCatalog</c>, so every client agrees on the
/// layout and the catalog can be exhaustively unit-tested. See #21.
/// </summary>
public static class RetroTemplateCatalog
{
    /// <summary>How many columns a custom layout may define — enough to be useful, few enough to render.</summary>
    public const int MaxCustomColumns = 6;

    /// <summary>Longest a column title may be.</summary>
    public const int MaxColumnTitleLength = 60;

    private static readonly IReadOnlyList<string> WentWellToImprove =
        new[] { "Went well", "To improve", "Action items" };

    private static readonly IReadOnlyList<string> StartStopContinue =
        new[] { "Start", "Stop", "Continue" };

    private static readonly IReadOnlyList<string> FourLs =
        new[] { "Liked", "Learned", "Lacked", "Longed for" };

    private static readonly IReadOnlyList<string> MadSadGlad =
        new[] { "Mad", "Sad", "Glad" };

    /// <summary>
    /// The column titles for a template, in display order. For <see cref="RetroTemplate.Custom"/>,
    /// <paramref name="customColumns"/> is a comma-separated list of titles.
    /// </summary>
    /// <exception cref="ArgumentException">A custom layout with no usable titles, or too many.</exception>
    public static IReadOnlyList<string> GetColumns(RetroTemplate template, string? customColumns = null) =>
        template switch
        {
            RetroTemplate.WentWellToImprove => WentWellToImprove,
            RetroTemplate.StartStopContinue => StartStopContinue,
            RetroTemplate.FourLs => FourLs,
            RetroTemplate.MadSadGlad => MadSadGlad,
            RetroTemplate.Custom => ParseCustom(customColumns),
            _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown retro template."),
        };

    private static IReadOnlyList<string> ParseCustom(string? customColumns)
    {
        var titles = (customColumns ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Length > MaxColumnTitleLength ? t[..MaxColumnTitleLength] : t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (titles.Length == 0)
        {
            throw new ArgumentException("A custom board must define at least one column.", nameof(customColumns));
        }

        if (titles.Length > MaxCustomColumns)
        {
            throw new ArgumentException(
                $"A custom board may define at most {MaxCustomColumns} columns.", nameof(customColumns));
        }

        return titles;
    }
}
