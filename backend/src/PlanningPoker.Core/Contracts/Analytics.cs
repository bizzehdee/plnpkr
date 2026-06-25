namespace PlanningPoker.Core.Contracts;

/// <summary>One completed round in the session history (#11).</summary>
public record RoundResultInfo(
    string? Story,
    string? Note,
    string? FinalEstimate,
    double? Average,
    bool Consensus,
    int VoteCount,
    DateTimeOffset RecordedAt);

/// <summary>
/// Velocity/throughput summary for a session (#11): how many items were estimated, how often the team
/// reached consensus, and the full per-round history (newest first).
/// </summary>
public record SessionAnalytics(
    string ShortCode,
    string Name,
    int RoundsCompleted,
    int ConsensusRounds,
    double ConsensusRate,
    double? AverageVotesPerRound,
    IReadOnlyList<RoundResultInfo> Rounds);
