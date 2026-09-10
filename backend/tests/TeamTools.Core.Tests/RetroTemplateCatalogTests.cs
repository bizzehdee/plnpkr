using FluentAssertions;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// The retro column catalog (#21) — pure, so exhaustively testable, exactly as `DeckCatalog` is.
/// </summary>
public class RetroTemplateCatalogTests
{
    [Theory]
    [InlineData(RetroTemplate.WentWellToImprove, 3)]
    [InlineData(RetroTemplate.StartStopContinue, 3)]
    [InlineData(RetroTemplate.FourLs, 4)]
    [InlineData(RetroTemplate.MadSadGlad, 3)]
    public void Every_built_in_template_resolves_to_columns(RetroTemplate template, int expected)
    {
        var columns = RetroTemplateCatalog.GetColumns(template);

        columns.Should().HaveCount(expected);
        columns.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c));
    }

    [Fact]
    public void A_custom_layout_trims_and_drops_blank_entries()
    {
        var columns = RetroTemplateCatalog.GetColumns(RetroTemplate.Custom, " Keep , , Drop ,");

        columns.Should().Equal("Keep", "Drop");
    }

    [Fact]
    public void A_custom_layout_drops_duplicate_titles_case_insensitively()
    {
        // Two columns with the same name are indistinguishable on the board and in an export.
        var columns = RetroTemplateCatalog.GetColumns(RetroTemplate.Custom, "Keep, keep, Drop");

        columns.Should().Equal("Keep", "Drop");
    }

    [Fact]
    public void An_over_long_custom_title_is_truncated_rather_than_rejected()
    {
        var columns = RetroTemplateCatalog.GetColumns(
            RetroTemplate.Custom, new string('x', RetroTemplateCatalog.MaxColumnTitleLength + 20));

        columns.Should().ContainSingle()
            .Which.Should().HaveLength(RetroTemplateCatalog.MaxColumnTitleLength);
    }

    [Fact]
    public void A_custom_layout_with_no_usable_titles_throws()
    {
        var act = () => RetroTemplateCatalog.GetColumns(RetroTemplate.Custom, " , , ");

        act.Should().Throw<ArgumentException>().WithMessage("*at least one column*");
    }

    [Fact]
    public void A_custom_layout_with_too_many_columns_throws()
    {
        var tooMany = string.Join(',', Enumerable.Range(1, RetroTemplateCatalog.MaxCustomColumns + 1)
            .Select(i => $"Column {i}"));

        var act = () => RetroTemplateCatalog.GetColumns(RetroTemplate.Custom, tooMany);

        act.Should().Throw<ArgumentException>().WithMessage($"*at most {RetroTemplateCatalog.MaxCustomColumns}*");
    }

    [Fact]
    public void The_maximum_number_of_custom_columns_is_allowed()
    {
        var atLimit = string.Join(',', Enumerable.Range(1, RetroTemplateCatalog.MaxCustomColumns)
            .Select(i => $"Column {i}"));

        RetroTemplateCatalog.GetColumns(RetroTemplate.Custom, atLimit)
            .Should().HaveCount(RetroTemplateCatalog.MaxCustomColumns);
    }

    [Fact]
    public void An_unknown_template_value_throws_rather_than_silently_producing_no_columns()
    {
        var act = () => RetroTemplateCatalog.GetColumns((RetroTemplate)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
