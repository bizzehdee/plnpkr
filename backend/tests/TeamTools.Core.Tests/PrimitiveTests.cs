using FluentAssertions;
using TeamTools.Core;
using TeamTools.Core.Poker;
using TeamTools.Core.Retro;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// The room-level primitives both tools build on (#34). Small, pure and table-driven: they are
/// shared, so a change here reaches every tool at once, which is exactly the property that makes
/// them worth extracting and worth pinning.
/// </summary>
public class CountdownTests
{
    [Theory]
    [InlineData(60, 60)]      // inside the range
    [InlineData(1, 5)]        // below the floor
    [InlineData(999_999, 60)] // above the ceiling
    public void Normalize_clamps_into_the_callers_range(int requested, int expected)
    {
        Countdown.Normalize(requested, 5, 60).Should().Be(expected);
    }

    [Fact]
    public void Normalize_keeps_null_meaning_no_countdown()
    {
        // The distinction matters: null is "no timer", not "a timer of zero".
        Countdown.Normalize(null, 5, 60).Should().BeNull();
    }

    [Fact]
    public void The_bounds_stay_the_tools_own()
    {
        // The clamp is shared; the policy is not. A 10-second retro phase is not a phase, but a
        // 10-second poker round timer is a perfectly good one.
        PokerRoundRules.NormalizeTimerDuration(10).Should().Be(10);
        RetroPhaseRules.NormalizeDuration(10).Should().Be(RetroPhaseRules.MinPhaseSeconds);
    }

    [Fact]
    public void DeadlineFrom_turns_a_length_into_one_instant_clients_can_tick_against()
    {
        var now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        Countdown.DeadlineFrom(90, now).Should().Be(now.AddSeconds(90));
        Countdown.DeadlineFrom(null, now).Should().BeNull();
    }
}

/// <summary>
/// CSV escaping, shared by both exports (#34). It was duplicated character-for-character before,
/// and a spreadsheet silently losing a column is a bug nobody reports as one.
/// </summary>
public class CsvTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has\nnewline", "\"has\nnewline\"")]
    [InlineData("has\rreturn", "\"has\rreturn\"")]
    [InlineData("slow, flaky, and \"loud\"", "\"slow, flaky, and \"\"loud\"\"\"")]
    public void Field_quotes_only_what_needs_quoting(string? value, string expected)
    {
        Csv.Field(value).Should().Be(expected);
    }

    [Fact]
    public void Row_escapes_every_field_and_joins_them()
    {
        Csv.Row("a", "b,c", null, "d").Should().Be("a,\"b,c\",,d");
    }

    [Fact]
    public void Date_is_invariant_so_an_export_reads_the_same_everywhere()
    {
        // A locale-shaped date in a CSV is how 03/04 becomes two different days downstream.
        Csv.Date(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero)).Should().Be("2026-03-04");
        Csv.Date(null).Should().BeEmpty();
    }
}
