namespace TeamTools.Core.Models;

/// <summary>
/// A completed estimation round, recorded when a revealed round is reset to move on (#11). Builds the
/// session's history for velocity/throughput analytics and export (#12).
/// </summary>
public class RoundResult
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    /// <summary>The story/title estimated, if one was set.</summary>
    public string? Story { get; set; }

    /// <summary>The story note at the time the round completed, if any (#10).</summary>
    public string? Note { get; set; }

    /// <summary>The agreed estimate: the consensus card when unanimous, otherwise null.</summary>
    public string? FinalEstimate { get; set; }

    /// <summary>Mean of the numeric votes, or null when none were numeric.</summary>
    public double? Average { get; set; }

    public bool Consensus { get; set; }

    /// <summary>How many votes were cast in the round.</summary>
    public int VoteCount { get; set; }

    /// <summary>When the round was recorded (UTC).</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public Session? Session { get; set; }
}
