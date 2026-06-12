using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Models;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class StatsCalculatorTests
{
    private static Participant Voted(string vote) =>
        new() { Role = ParticipantRole.Voter, HasVoted = true, Vote = vote };

    private static Participant NotVoted() =>
        new() { Role = ParticipantRole.Voter, HasVoted = false, Vote = null };

    [Fact]
    public void Average_is_mean_of_numeric_votes()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("2"), Voted("4"), Voted("6") });

        stats.Average.Should().Be(4);
        stats.VoteCount.Should().Be(3);
    }

    [Fact]
    public void Non_numeric_votes_are_excluded_from_average_but_counted()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("5"), Voted("?"), Voted("☕") });

        stats.Average.Should().Be(5); // only "5" contributes to the mean
        stats.VoteCount.Should().Be(3);
    }

    [Fact]
    public void Half_card_counts_as_zero_point_five()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("½"), Voted("½") });

        stats.Average.Should().Be(0.5);
    }

    [Fact]
    public void Average_is_null_when_no_numeric_votes()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("XS"), Voted("?") });

        stats.Average.Should().BeNull();
    }

    [Fact]
    public void Consensus_true_when_all_voters_chose_the_same_card()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("5"), Voted("5"), Voted("5") });

        stats.Consensus.Should().BeTrue();
    }

    [Fact]
    public void Consensus_false_when_votes_differ()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("5"), Voted("8") });

        stats.Consensus.Should().BeFalse();
    }

    [Fact]
    public void Consensus_false_with_a_single_vote()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("5") });

        stats.Consensus.Should().BeFalse();
    }

    [Fact]
    public void Distribution_counts_each_card_and_orders_by_frequency()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("5"), Voted("5"), Voted("8") });

        stats.Distribution[0].Should().Be(new Contracts.VoteCount("5", 2));
        stats.Distribution[1].Should().Be(new Contracts.VoteCount("8", 1));
    }

    [Fact]
    public void Participants_who_have_not_voted_are_ignored()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("4"), NotVoted() });

        stats.VoteCount.Should().Be(1);
        stats.Average.Should().Be(4);
    }

    // --- Min / Max / StdDev ------------------------------------------------

    [Fact]
    public void Min_max_stddev_are_computed_over_numeric_votes()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("2"), Voted("4"), Voted("6") });

        stats.Min.Should().Be(2);
        stats.Max.Should().Be(6);
        stats.StdDev.Should().BeApproximately(1.632, 0.001); // population stddev of {2,4,6}
    }

    [Fact]
    public void Numeric_stats_are_null_when_no_numeric_votes()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("?"), Voted("☕") });

        stats.Min.Should().BeNull();
        stats.Max.Should().BeNull();
        stats.StdDev.Should().BeNull();
    }

    // --- Outliers (#44) ----------------------------------------------

    [Fact]
    public void Consensus_has_no_outliers()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("5"), Voted("5"), Voted("5") });

        stats.OutlierValues.Should().BeEmpty();
    }

    [Fact]
    public void A_pair_of_differing_votes_has_no_outliers()
    {
        // Each sits exactly one stddev from the mean — not *greater than* — so neither is flagged.
        var stats = StatsCalculator.Compute(new[] { Voted("3"), Voted("8") });

        stats.OutlierValues.Should().BeEmpty();
    }

    [Fact]
    public void A_lone_high_estimate_is_flagged_as_an_outlier()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("3"), Voted("5"), Voted("13") });

        stats.OutlierValues.Should().Contain("13").And.NotContain("3").And.NotContain("5");
    }

    [Fact]
    public void Both_ends_of_a_three_way_split_are_flagged()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("3"), Voted("5"), Voted("8") });

        stats.OutlierValues.Should().BeEquivalentTo(new[] { "3", "8" });
    }

    [Fact]
    public void A_single_diverging_voter_among_a_cluster_is_flagged()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("5"), Voted("5"), Voted("5"), Voted("13") });

        stats.OutlierValues.Should().BeEquivalentTo(new[] { "13" });
    }

    [Fact]
    public void Non_numeric_votes_are_never_outliers()
    {
        var stats = StatsCalculator.Compute(new[] { Voted("3"), Voted("5"), Voted("13"), Voted("?") });

        stats.OutlierValues.Should().NotContain("?");
    }
}
