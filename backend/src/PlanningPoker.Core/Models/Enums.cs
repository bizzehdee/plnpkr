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

/// <summary>
/// The round's phase. Voting hides cards; Revealed shows them (not a lock — votes can still change, #23);
/// Discussion is a distinct timed talk-it-through phase between a reveal and a re-vote (#9).
/// </summary>
public enum SessionState
{
    Voting,
    Revealed,
    Discussion,
}

/// <summary>A participant either estimates (Voter) or watches (Observer). See #8.</summary>
public enum ParticipantRole
{
    Voter,
    Observer,
}
