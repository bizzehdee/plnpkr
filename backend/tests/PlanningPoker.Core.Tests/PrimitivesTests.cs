using FluentAssertions;
using PlanningPoker.Core;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Alice", "alice")]
    [InlineData("  Bob  ", "bob")]
    [InlineData("CAROL", "carol")]
    public void Normalize_trims_and_lowercases(string input, string expected)
    {
        NameNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("x", false)]
    public void IsBlank_detects_empty_or_whitespace(string? input, bool expected)
    {
        NameNormalizer.IsBlank(input).Should().Be(expected);
    }
}

public class ShortCodeGeneratorTests
{
    [Fact]
    public void Generate_produces_adjective_noun_number_shape()
    {
        // Seeded Random => deterministic output, so the assertion is stable.
        var generator = new ShortCodeGenerator(new Random(12345));

        var code = generator.Generate();

        code.Should().MatchRegex("^[a-z]+-[a-z]+-[0-9]{2}$");
    }

    [Fact]
    public void Generate_varies_across_calls()
    {
        var generator = new ShortCodeGenerator(new Random(1));

        var codes = Enumerable.Range(0, 20).Select(_ => generator.Generate()).ToList();

        codes.Distinct().Should().HaveCountGreaterThan(1);
    }
}
