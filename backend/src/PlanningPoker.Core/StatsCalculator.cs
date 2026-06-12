using System.Globalization;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;

namespace PlanningPoker.Core;

/// <summary>
/// Computes reveal statistics over the current voter votes. Pure and deterministic. Only numeric
/// cards count toward the average; non-numeric cards (?, ☕, T-shirt sizes) are ignored. See #28.
/// </summary>
public static class StatsCalculator
{
    /// <summary>
    /// Builds stats from the participants who have an active vote. Observers never have a vote, so
    /// they are naturally excluded.
    /// </summary>
    public static VoteStats Compute(IEnumerable<Participant> participants)
    {
        var votes = participants
            .Where(p => p.HasVoted && p.Vote is not null)
            .Select(p => p.Vote!)
            .ToList();

        // Parse each card to a number exactly once and reuse it for the aggregates and outlier check —
        // a revealed snapshot is broadcast to the whole group, so avoid re-parsing the same cards.
        var parsed = new (string Card, double? Numeric)[votes.Count];
        var numericValues = new List<double>(votes.Count);
        for (var i = 0; i < votes.Count; i++)
        {
            var numeric = TryParseNumeric(votes[i]);
            parsed[i] = (votes[i], numeric);
            if (numeric.HasValue)
            {
                numericValues.Add(numeric.Value);
            }
        }

        double? average = numericValues.Count > 0 ? numericValues.Average() : null;
        double? min = numericValues.Count > 0 ? numericValues.Min() : null;
        double? max = numericValues.Count > 0 ? numericValues.Max() : null;

        // Population standard deviation of the numeric votes.
        double? stdDev = null;
        if (numericValues.Count > 0 && average is double mean)
        {
            var variance = numericValues.Average(v => (v - mean) * (v - mean));
            stdDev = Math.Sqrt(variance);
        }

        // Outliers: card values whose numeric deviation from the mean exceeds the standard deviation.
        // This naturally flags nothing on consensus (stddev 0) or a two-vote split (deviation == stddev),
        // and flags the genuinely divergent estimate(s) once there are three or more. See #44.
        var outlierValues = Array.Empty<string>() as IReadOnlyList<string>;
        if (numericValues.Count > 0 && average is double m && stdDev is double sd && sd > 0)
        {
            outlierValues = parsed
                .Where(x => x.Numeric is double num && Math.Abs(num - m) > sd)
                .Select(x => x.Card)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        // Consensus: at least two votes cast and everyone who voted chose the same card.
        var consensus = votes.Count >= 2 && votes.Distinct(StringComparer.Ordinal).Count() == 1;

        var distribution = votes
            .GroupBy(v => v, StringComparer.Ordinal)
            .Select(g => new VoteCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Value, StringComparer.Ordinal)
            .ToArray();

        return new VoteStats(average, consensus, votes.Count, distribution, min, max, stdDev, outlierValues);
    }

    /// <summary>Parses a card to a number, treating "½" as 0.5. Returns null for non-numeric cards.</summary>
    public static double? TryParseNumeric(string card)
    {
        if (card == "½")
        {
            return 0.5;
        }

        return double.TryParse(card, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
