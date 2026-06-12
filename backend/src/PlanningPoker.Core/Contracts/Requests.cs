using PlanningPoker.Core.Models;

namespace PlanningPoker.Core.Contracts;

/// <summary>Request to create a new session. The creator becomes its first participant. See #1.</summary>
public record CreateSessionRequest(
    string Name,
    DeckType DeckType,
    string? CustomCards,
    string CreatorUserId,
    string CreatorDisplayName,
    bool Organise,
    string? Password = null, // optional join password (plaintext in transit only); hashed before storage (#2)
    bool EnableReactions = true, // initial emoji-reactions state, organiser-set at creation (#17)
    int? TimerDurationSeconds = null); // optional round-timer length configured at creation (#14)

/// <summary>Request to join an existing session by short code. See #6.</summary>
public record JoinSessionRequest(
    string ShortCode,
    string UserId,
    string DisplayName,
    ParticipantRole Role,
    string? Password = null); // required only when the session has a password and the user is new (#2)
