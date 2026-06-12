namespace PlanningPoker.Core.Models;

/// <summary>The built-in estimation card sets a session can use. See #32.</summary>
public enum DeckType
{
    Sequential,
    Fibonacci,
    ModifiedFibonacci,
    TShirt,
    PowersOfTwo,
    Custom,
}

/// <summary>Whether votes are hidden or shown. Revealed is NOT a lock — votes can still change. See #23.</summary>
public enum SessionState
{
    Voting,
    Revealed,
}

/// <summary>A participant either estimates (Voter) or watches (Observer). See #8.</summary>
public enum ParticipantRole
{
    Voter,
    Observer,
}
