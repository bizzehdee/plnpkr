using System.Globalization;
using System.Text;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;

namespace TeamTools.Core.Poker;

/// <summary>
/// Planning Poker decision-making (#19): the round state machine (vote / reveal / reset /
/// discussion), the deck, the item under estimation, the round timer, and the completed-round
/// history behind analytics and export.
/// <para>
/// Room concerns — join, leave, presence, roles, organisers, password, close/delete — are delegated
/// to <see cref="RoomService"/>; this service wraps those so the hub keeps one tool-shaped API and
/// every result carries a poker <see cref="SessionSnapshot"/>. Pure of transport/EF concerns, so it
/// is unit-testable against an in-memory <see cref="IRoomStore"/>.
/// </para>
/// </summary>
public class PokerService
{
    private readonly IRoomStore _store;
    private readonly RoomService _rooms;
    private readonly IClock _clock;

    public PokerService(IRoomStore store, RoomService rooms, IClock clock)
    {
        _store = store;
        _rooms = rooms;
        _clock = clock;
    }

    /// <summary>Bounds for a round timer (seconds): at least 5s, at most one hour.</summary>
    public const int MinTimerSeconds = PokerRoundRules.MinTimerSeconds;
    public const int MaxTimerSeconds = PokerRoundRules.MaxTimerSeconds;

    // --- Creation ----------------------------------------------------------

    public async Task<CreateSessionResult> CreateAsync(CreateSessionRequest request, CancellationToken ct = default)
    {
        if (NameNormalizer.IsBlank(request.Name))
        {
            return CreateSessionResult.InvalidName("Session name is required.");
        }

        if (NameNormalizer.IsBlank(request.CreatorDisplayName))
        {
            return CreateSessionResult.InvalidName("Your display name is required.");
        }

        // Validate the deck up front (custom decks can be empty / unparseable).
        try
        {
            _ = DeckCatalog.GetCards(request.DeckType, request.CustomCards);
        }
        catch (ArgumentException ex)
        {
            return CreateSessionResult.InvalidDeck(ex.Message);
        }

        var room = await _rooms.NewRoomAsync(
            RoomTool.Poker, request.Name, request.CreatorUserId, request.CreatorDisplayName,
            request.Organise, request.EnableReactions, request.Password, ct);

        room.PokerRound = new PokerRound
        {
            RoomId = room.Id,
            DeckType = request.DeckType,
            CustomCards = request.DeckType == DeckType.Custom ? request.CustomCards : null,
            State = SessionState.Voting,
            AutoReveal = false,
            TimerDurationSeconds = PokerRoundRules.NormalizeTimerDuration(request.TimerDurationSeconds),
        };

        await _rooms.AddAsync(room, ct);
        return CreateSessionResult.Ok(ToSnapshot(room));
    }

    // --- Room-level operations, projected as poker results -----------------

    public async Task<JoinResult> JoinAsync(JoinSessionRequest request, CancellationToken ct = default)
    {
        var outcome = await _rooms.JoinAsync(request, ct);
        if (outcome.Status != JoinStatus.Ok)
        {
            return new JoinResult(outcome.Status, null, null, outcome.Error);
        }

        var room = outcome.Room!;
        return JoinResult.Ok(
            ToSnapshot(room),
            RoomProjection.ToInfo(outcome.Participant!, room.PokerRound?.IsRevealed ?? false));
    }

    public async Task<LeaveResult> LeaveAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (status, room) = await _rooms.LeaveAsync(shortCode, userId, ct);
        return status switch
        {
            LeaveStatus.Ok => LeaveResult.Ok(ToSnapshot(room!)),
            LeaveStatus.SessionNotFound => LeaveResult.NotFound(),
            _ => LeaveResult.NotInSession(),
        };
    }

    public async Task<SessionActionResult> MarkDisconnectedAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.MarkDisconnectedAsync(shortCode, userId, ct));

    /// <summary>
    /// Switch a participant's role (#21). Becoming an observer clears their vote, and the changed
    /// voter set may complete the round — both supplied to the room engine as tool hooks.
    /// </summary>
    public async Task<SessionActionResult> ChangeRoleAsync(
        string shortCode, string actingUserId, string targetUserId, ParticipantRole role,
        CancellationToken ct = default) =>
        Project(await _rooms.ChangeRoleAsync(
            shortCode, actingUserId, targetUserId, role,
            onBecameObserver: PokerRoundRules.ClearVote,
            // The voter set changed — a pending round may now be complete (e.g. the last unvoted
            // voter became an observer). Re-evaluate the auto-reveal gate. See #18.
            afterChange: PokerRoundRules.MaybeAutoReveal,
            ct));

    public async Task<SessionActionResult> SetAllowRoleChangeAsync(
        string shortCode, string userId, bool enabled, CancellationToken ct = default) =>
        Project(await _rooms.SetAllowRoleChangeAsync(shortCode, userId, enabled, ct));

    public async Task<SessionActionResult> SetReactionsEnabledAsync(
        string shortCode, string userId, bool enabled, CancellationToken ct = default) =>
        Project(await _rooms.SetReactionsEnabledAsync(shortCode, userId, enabled, ct));

    public Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken ct = default) =>
        _rooms.AreReactionsEnabledAsync(shortCode, ct);

    public async Task<SessionActionResult> PromoteToOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.PromoteToOrganiserAsync(shortCode, actingUserId, targetUserId, ct));

    public async Task<SessionActionResult> DemoteOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.DemoteOrganiserAsync(shortCode, actingUserId, targetUserId, ct));

    public async Task<SessionActionResult> TransferOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.TransferOrganiserAsync(shortCode, actingUserId, targetUserId, ct));

    public async Task<SessionActionResult> SetPasswordAsync(
        string shortCode, string userId, string? newPassword, CancellationToken ct = default) =>
        Project(await _rooms.SetPasswordAsync(shortCode, userId, newPassword, ct));

    /// <summary>
    /// Organiser-only: close the session into a frozen read-only state (#26). Stops any running timer
    /// so it can't auto-reveal a frozen session.
    /// </summary>
    public async Task<SessionActionResult> CloseSessionAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.CloseRoomAsync(shortCode, userId, onClosed: room =>
        {
            if (room.PokerRound is { } round)
            {
                PokerRoundRules.StopRunningTimer(round);
            }
        }, ct));

    public async Task<SessionActionResult> DeleteSessionAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.DeleteRoomAsync(shortCode, userId, ct));

    public Task<SessionLanding?> GetLandingAsync(string shortCode, CancellationToken ct = default) =>
        _rooms.GetLandingAsync(shortCode, ct);

    // --- Voting & reveal ---------------------------------------------------

    /// <summary>Sets the caller's own vote. Observers are rejected; the card must be in the deck.</summary>
    public async Task<SessionActionResult> CastVoteAsync(
        string shortCode, string userId, string card, CancellationToken ct = default)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        if (room is null)
        {
            return SessionActionResult.NotFound();
        }

        var participant = room.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant is null)
        {
            return SessionActionResult.NotParticipant();
        }

        if (room.ClosedAt is not null)
        {
            return SessionActionResult.SessionClosed(); // read-only (#26)
        }

        if (participant.Role == ParticipantRole.Observer)
        {
            return SessionActionResult.ObserverCannotVote();
        }

        var round = Round(room);
        if (!DeckCatalog.GetCards(round.DeckType, round.CustomCards).Contains(card))
        {
            return SessionActionResult.InvalidCard();
        }

        participant.Vote = card;
        participant.HasVoted = true;

        if (round.State == SessionState.Revealed)
        {
            // Re-estimating during the discussion — flag the changed card. See #23.
            participant.ChangedAfterReveal = true;
        }
        else
        {
            PokerRoundRules.MaybeAutoReveal(room);
        }

        return await CommitAsync(room, ct);
    }

    public Task<SessionActionResult> RevealAsync(string shortCode, string userId, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (room, round) =>
        {
            round.State = SessionState.Revealed;
            PokerRoundRules.StopRunningTimer(round); // the round is over — stop any countdown
        }, ct);

    /// <summary>Clears every vote and starts a fresh round. The auto-reveal flag and story persist.</summary>
    public Task<SessionActionResult> ResetRoundAsync(string shortCode, string userId, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (room, round) =>
        {
            // Resetting a revealed round means the team is done with this item — capture it for
            // analytics before clearing the votes. See #11.
            if (round.State == SessionState.Revealed)
            {
                RecordCompletedRound(room, round);
            }

            foreach (var p in room.Participants)
            {
                PokerRoundRules.ClearVote(p);
            }

            round.State = SessionState.Voting;
            // A fresh round starts with no countdown until the organiser starts one.
            PokerRoundRules.StopRunningTimer(round);
        }, ct);

    /// <summary>Clears a single participant's vote so they can re-cast (used during discussion).</summary>
    public async Task<SessionActionResult> ResetVoteAsync(
        string shortCode, string userId, string targetUserId, CancellationToken ct = default)
    {
        var (room, error) = await _rooms.LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return Project(error);
        }

        var target = room!.Participants.FirstOrDefault(p => p.UserId == targetUserId);
        if (target is null)
        {
            return SessionActionResult.TargetNotFound();
        }

        PokerRoundRules.ClearVote(target);
        return await CommitAsync(room, ct);
    }

    /// <summary>
    /// Organiser-only: enter the timed discussion phase (#9), typically after a non-consensus reveal.
    /// Optionally starts a countdown (<paramref name="seconds"/> or the configured duration) that, on
    /// expiry, auto-advances to a fresh re-vote. Votes are left intact so the cards stay on screen.
    /// </summary>
    public Task<SessionActionResult> StartDiscussionAsync(
        string shortCode, string userId, int? seconds = null, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (room, round) =>
        {
            round.State = SessionState.Discussion;

            var duration = PokerRoundRules.NormalizeTimerDuration(seconds) ?? round.TimerDurationSeconds;
            if (duration is not null)
            {
                round.TimerDurationSeconds = duration;
                round.TimerPausedRemainingSeconds = null;
                round.TimerDeadline = _clock.UtcNow.AddSeconds(duration.Value);
            }
        }, ct);

    /// <summary>
    /// Organiser-only: end the discussion phase and start a fresh re-vote (Discussion → Voting),
    /// clearing every vote and stopping the countdown. No-op (Ok) if not currently discussing. See #9.
    /// </summary>
    public Task<SessionActionResult> EndDiscussionAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (room, round) =>
        {
            if (round.State != SessionState.Discussion)
            {
                return;
            }

            foreach (var p in room.Participants)
            {
                PokerRoundRules.ClearVote(p);
            }

            round.State = SessionState.Voting;
            PokerRoundRules.StopRunningTimer(round);
        }, ct);

    public Task<SessionActionResult> SetAutoRevealAsync(
        string shortCode, string userId, bool enabled, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (room, round) =>
        {
            round.AutoReveal = enabled;
            if (enabled)
            {
                // Turning it on with all votes already in reveals immediately. See #18.
                PokerRoundRules.MaybeAutoReveal(room);
            }
        }, ct);

    /// <summary>
    /// Organiser-only: swap the active deck mid-session (#11). Validates via <see cref="DeckCatalog"/>;
    /// because prior cards may no longer be valid, this **resets the round** (clears every vote, back to
    /// Voting) and stops any running timer. The new deck rides the broadcast snapshot.
    /// </summary>
    public async Task<SessionActionResult> SetDeckAsync(
        string shortCode, string userId, DeckType deckType, string? customCards, CancellationToken ct = default)
    {
        var (room, error) = await _rooms.LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return Project(error);
        }

        // Reject an unusable deck (e.g. an empty custom list) before mutating anything.
        try
        {
            _ = DeckCatalog.GetCards(deckType, customCards);
        }
        catch (ArgumentException)
        {
            return SessionActionResult.InvalidDeck();
        }

        var round = Round(room!);
        round.DeckType = deckType;
        round.CustomCards = deckType == DeckType.Custom ? customCards : null;

        // Prior votes were cast against the old deck — clear them and start a fresh round.
        foreach (var p in room!.Participants)
        {
            PokerRoundRules.ClearVote(p);
        }

        round.State = SessionState.Voting;
        PokerRoundRules.StopRunningTimer(round);

        return await CommitAsync(room, ct);
    }

    public Task<SessionActionResult> SetStoryAsync(
        string shortCode, string userId, string? title, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (_, round) =>
        {
            var newStory = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
            // A different story starts with a fresh (empty) note — the old note belonged to the old
            // story (#10).
            if (!string.Equals(round.CurrentStory, newStory, StringComparison.Ordinal))
            {
                round.CurrentStoryNote = null;
            }
            round.CurrentStory = newStory;
        }, ct);

    /// <summary>
    /// Set or clear the free-text note for the current story (#10). Collaborative: any participant may
    /// edit it (not organiser-gated), as long as the session isn't closed.
    /// </summary>
    public async Task<SessionActionResult> SetStoryNoteAsync(
        string shortCode, string userId, string? note, CancellationToken ct = default)
    {
        var (room, error) = await _rooms.LoadForParticipantAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return Project(error);
        }

        Round(room!).CurrentStoryNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return await CommitAsync(room!, ct);
    }

    // --- Round timer (organiser-controlled, #14) ---------------------------

    /// <summary>
    /// Organiser-only: set or clear (null) the configured round-timer duration without starting it.
    /// This is the "changeable mid-session" control; it also seeds restarts. See #14.
    /// </summary>
    public Task<SessionActionResult> SetTimerDurationAsync(
        string shortCode, string userId, int? seconds, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (_, round) =>
            round.TimerDurationSeconds = PokerRoundRules.NormalizeTimerDuration(seconds), ct);

    /// <summary>
    /// Organiser-only: start (or restart) the round timer. Uses <paramref name="seconds"/> when given
    /// (also updating the configured duration), otherwise the configured duration. No-op-ish if neither
    /// is available. Clears any paused state. See #14.
    /// </summary>
    public Task<SessionActionResult> StartTimerAsync(
        string shortCode, string userId, int? seconds = null, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (_, round) =>
        {
            var duration = PokerRoundRules.NormalizeTimerDuration(seconds) ?? round.TimerDurationSeconds;
            if (duration is null)
            {
                // Nothing to start with — leave the timer untouched.
                return;
            }

            round.TimerDurationSeconds = duration;
            round.TimerPausedRemainingSeconds = null;
            round.TimerDeadline = _clock.UtcNow.AddSeconds(duration.Value);
        }, ct);

    /// <summary>Organiser-only: pause a running timer, freezing the seconds left. See #14.</summary>
    public Task<SessionActionResult> PauseTimerAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (_, round) =>
        {
            if (round.TimerDeadline is { } deadline)
            {
                var remaining = (int)Math.Ceiling((deadline - _clock.UtcNow).TotalSeconds);
                round.TimerPausedRemainingSeconds = Math.Max(0, remaining);
                round.TimerDeadline = null;
            }
        }, ct);

    /// <summary>Organiser-only: resume a paused timer from the frozen remaining time. See #14.</summary>
    public Task<SessionActionResult> ResumeTimerAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (_, round) =>
        {
            if (round.TimerPausedRemainingSeconds is { } remaining)
            {
                round.TimerDeadline = _clock.UtcNow.AddSeconds(Math.Max(0, remaining));
                round.TimerPausedRemainingSeconds = null;
            }
        }, ct);

    /// <summary>Organiser-only: stop/cancel the timer (back to idle); the configured duration is kept. See #14.</summary>
    public Task<SessionActionResult> StopTimerAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, (_, round) => PokerRoundRules.StopRunningTimer(round), ct);

    // --- Reads -------------------------------------------------------------

    public async Task<SessionSnapshot?> GetByShortCodeAsync(string shortCode, CancellationToken ct = default)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        return room is null ? null : ToSnapshot(room);
    }

    /// <summary>
    /// Velocity/throughput analytics for a session (#11): counts, consensus rate, and the per-round
    /// history (newest first). Null if the session is unknown.
    /// </summary>
    public async Task<SessionAnalytics?> GetAnalyticsAsync(string shortCode, CancellationToken ct = default)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        if (room is null)
        {
            return null;
        }

        var rounds = (room.PokerRound?.RoundResults ?? new List<RoundResult>())
            .OrderByDescending(r => r.RecordedAt)
            .Select(r => new RoundResultInfo(
                r.Story, r.Note, r.FinalEstimate, r.Average, r.Consensus, r.VoteCount, r.RecordedAt))
            .ToList();

        var completed = rounds.Count;
        var consensusRounds = rounds.Count(r => r.Consensus);
        var consensusRate = completed == 0 ? 0d : (double)consensusRounds / completed;
        double? averageVotes = completed == 0 ? null : rounds.Average(r => r.VoteCount);

        return new SessionAnalytics(
            room.ShortCode, room.Name, completed, consensusRounds, consensusRate, averageVotes, rounds);
    }

    /// <summary>
    /// Renders the session's completed-round history as CSV for export (#12). Null if the session is
    /// unknown. Columns: RecordedAt, Story, FinalEstimate, Average, Consensus, VoteCount, Note.
    /// </summary>
    public async Task<string?> GetAnalyticsCsvAsync(string shortCode, CancellationToken ct = default)
    {
        var analytics = await GetAnalyticsAsync(shortCode, ct);
        if (analytics is null)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("RecordedAt,Story,FinalEstimate,Average,Consensus,VoteCount,Note");
        foreach (var r in analytics.Rounds)
        {
            sb.AppendLine(string.Join(',',
                Csv(r.RecordedAt.ToString("o", CultureInfo.InvariantCulture)),
                Csv(r.Story),
                Csv(r.FinalEstimate),
                Csv(r.Average?.ToString(CultureInfo.InvariantCulture)),
                Csv(r.Consensus ? "true" : "false"),
                Csv(r.VoteCount.ToString(CultureInfo.InvariantCulture)),
                Csv(r.Note)));
        }

        return sb.ToString();
    }

    /// <summary>RFC-4180 CSV field: quote when it contains a comma, quote, CR or LF; double inner quotes.</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    // --- Helpers -----------------------------------------------------------

    /// <summary>
    /// The room's round. A poker room always has one (created with it, loaded with it); a missing
    /// round means the room is the wrong tool, which is a programming error rather than a user one.
    /// </summary>
    private static PokerRound Round(Room room) =>
        room.PokerRound ?? throw new InvalidOperationException(
            $"Room '{room.ShortCode}' hosts {room.Tool}, not Poker — it has no poker round.");

    /// <summary>Organiser-gated load, mutate the round, commit.</summary>
    private async Task<SessionActionResult> ControlAsync(
        string shortCode, string userId, Action<Room, PokerRound> mutate, CancellationToken ct)
    {
        var (room, error) = await _rooms.LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return Project(error);
        }

        mutate(room!, Round(room!));
        return await CommitAsync(room!, ct);
    }

    private async Task<SessionActionResult> CommitAsync(Room room, CancellationToken ct)
    {
        var outcome = await _rooms.CommitAsync(room, null, ct);
        return Project(outcome);
    }

    /// <summary>Turns a room-level outcome into a poker result, projecting the snapshot on success.</summary>
    private static SessionActionResult Project(RoomOutcome outcome) =>
        new(outcome.Status, outcome.Room is null ? null : ToSnapshot(outcome.Room));

    /// <summary>
    /// Appends a <see cref="RoundResult"/> capturing the just-revealed round's outcome (#11). Skips
    /// rounds where nobody voted. The final estimate is the consensus card when unanimous, else null.
    /// </summary>
    private void RecordCompletedRound(Room room, PokerRound round)
    {
        var stats = StatsCalculator.Compute(room.Participants);
        if (stats.VoteCount == 0)
        {
            return;
        }

        round.RoundResults.Add(new RoundResult
        {
            // Id left default so EF treats it as a new row (store-generated key); RoomId is set by
            // the relationship fixup from the tracked parent.
            RoomId = room.Id,
            Story = round.CurrentStory,
            Note = round.CurrentStoryNote,
            FinalEstimate = stats.Consensus && stats.Distribution.Count > 0 ? stats.Distribution[0].Value : null,
            Average = stats.Average,
            Consensus = stats.Consensus,
            VoteCount = stats.VoteCount,
            RecordedAt = _clock.UtcNow,
        });
    }

    // --- Projection --------------------------------------------------------

    public static SessionSnapshot ToSnapshot(Room room)
    {
        var round = Round(room);
        // Cards/stats stay visible through the discussion phase too — it's a post-reveal phase (#9).
        var revealed = round.IsRevealed;
        var stats = revealed ? StatsCalculator.Compute(room.Participants) : null;
        var outliers = stats?.OutlierValues ?? Array.Empty<string>();

        return new SessionSnapshot(
            RoomProjection.ToSnapshot(room, RoomProjection.ToInfos(room, revealed, outliers)),
            round.DeckType,
            DeckCatalog.GetCards(round.DeckType, round.CustomCards),
            round.State,
            round.AutoReveal,
            round.CurrentStory,
            round.CurrentStoryNote,
            stats,
            ToIntegrationInfo(round),
            round.TimerDurationSeconds,
            round.TimerDeadline,
            round.TimerPausedRemainingSeconds);
    }

    /// <summary>Broadcast-safe integration state (provider + linked issue); null when none. See #4.</summary>
    public static IntegrationInfo? ToIntegrationInfo(PokerRound round)
    {
        if (round.LinkedProvider is not { } provider)
        {
            return null;
        }

        var issue = round.LinkedIssue is { } li
            ? new LinkedIssueInfo(li.Key, li.Title, li.Description, li.Url, li.StoryPoints, li.StoryPointsFieldAvailable)
            : null;

        var selectedKey = round.LinkedIssue?.Key;
        var queue = round.TicketQueue
            .Select(q => new QueuedTicketInfo(q.Key, q.Title, q.Status, q.StoryPoints, q.Url,
                string.Equals(q.Key, selectedKey, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return new IntegrationInfo(provider, issue, queue);
    }

    /// <summary>Convenience for callers holding a room (e.g. the integration service). See #4.</summary>
    public static IntegrationInfo? ToIntegrationInfo(Room room) => ToIntegrationInfo(Round(room));
}
