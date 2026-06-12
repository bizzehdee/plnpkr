using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Security;

namespace PlanningPoker.Core;

/// <summary>
/// All session decision-making: create, join (with per-session name uniqueness and reconnect
/// reclaim), leave, and snapshot projection. Pure of transport/EF concerns so it is fully
/// unit-testable against an in-memory <see cref="ISessionStore"/>.
/// </summary>
public class SessionService
{
    private const int MaxShortCodeAttempts = 10;

    private readonly ISessionStore _store;
    private readonly IShortCodeGenerator _shortCodes;
    private readonly IClock _clock;
    private readonly IPasswordHasher _passwordHasher;

    public SessionService(ISessionStore store, IShortCodeGenerator shortCodes, IClock clock, IPasswordHasher? passwordHasher = null)
    {
        _store = store;
        _shortCodes = shortCodes;
        _clock = clock;
        _passwordHasher = passwordHasher ?? new Pbkdf2PasswordHasher();
    }

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

        var now = _clock.UtcNow;
        var session = new Session
        {
            Id = Guid.NewGuid(),
            ShortCode = await GenerateUniqueShortCodeAsync(ct),
            Name = request.Name.Trim(),
            DeckType = request.DeckType,
            CustomCards = request.DeckType == DeckType.Custom ? request.CustomCards : null,
            State = SessionState.Voting,
            OrganiserUserId = request.Organise ? request.CreatorUserId : null,
            AutoReveal = false,
            ReactionsEnabled = request.EnableReactions,
            TimerDurationSeconds = NormalizeTimerDuration(request.TimerDurationSeconds),
            // Hash an optional join password; the plaintext is never stored. See #2.
            PasswordHash = string.IsNullOrEmpty(request.Password) ? null : _passwordHasher.Hash(request.Password),
            CreatedAt = now,
            LastActivityAt = now,
        };

        session.Participants.Add(new Participant
        {
            SessionId = session.Id,
            UserId = request.CreatorUserId,
            DisplayName = request.CreatorDisplayName.Trim(),
            NormalizedName = NameNormalizer.Normalize(request.CreatorDisplayName),
            IsOrganiser = request.Organise,
            // An organiser runs the session rather than estimating, so they default to Observer.
            // A non-organising creator is a normal voter. See #10.
            Role = request.Organise ? ParticipantRole.Observer : ParticipantRole.Voter,
            IsConnected = true,
            LastSeenAt = now,
        });

        await _store.AddAsync(session, ct);
        return CreateSessionResult.Ok(ToSnapshot(session));
    }

    public async Task<JoinResult> JoinAsync(JoinSessionRequest request, CancellationToken ct = default)
    {
        if (NameNormalizer.IsBlank(request.DisplayName))
        {
            return JoinResult.InvalidName("A display name is required.");
        }

        var session = await _store.FindByShortCodeAsync(request.ShortCode, ct);
        if (session is null)
        {
            return JoinResult.NotFound();
        }

        var normalized = NameNormalizer.Normalize(request.DisplayName);
        var existing = session.Participants.FirstOrDefault(p => p.UserId == request.UserId);

        // A closed session is read-only (#26): no new joiners. An already-present participant may still
        // reconnect to view the frozen result.
        if (session.ClosedAt is not null && existing is null)
        {
            return JoinResult.SessionClosed();
        }

        // Password gate (#2): a new joiner must supply the correct password. An already-present
        // participant (e.g. reconnecting / auto-rejoin) is past the gate and isn't re-challenged.
        if (session.PasswordHash is { } hash && existing is null)
        {
            if (string.IsNullOrEmpty(request.Password))
            {
                return JoinResult.PasswordRequired();
            }
            if (!_passwordHasher.Verify(hash, request.Password))
            {
                return JoinResult.WrongPassword();
            }
        }

        // Reject if the name is used by a DIFFERENT participant (case-insensitive). The caller
        // re-joining with their own userId is allowed to keep their name. See #7.
        var nameClash = session.Participants.Any(p =>
            p.NormalizedName == normalized && p.UserId != request.UserId);
        if (nameClash)
        {
            return JoinResult.NameTaken();
        }

        var now = _clock.UtcNow;
        Participant participant;
        if (existing is not null)
        {
            // Reconnect / re-join: reclaim the seat (vote and ChangedAfterReveal are untouched),
            // applying the latest name and role and marking them connected again.
            existing.DisplayName = request.DisplayName.Trim();
            existing.NormalizedName = normalized;
            existing.Role = request.Role;
            participant = existing;
        }
        else
        {
            participant = new Participant
            {
                SessionId = session.Id,
                UserId = request.UserId,
                DisplayName = request.DisplayName.Trim(),
                NormalizedName = normalized,
                Role = request.Role,
            };
            session.Participants.Add(participant);
        }

        participant.IsConnected = true;
        participant.LastSeenAt = now;
        // Reconcile organiser flag from the session (the organiser reclaims their role on reconnect,
        // even if their participant row was evicted while away). See #34.
        participant.IsOrganiser = session.OrganiserUserId == request.UserId;

        session.LastActivityAt = now;

        try
        {
            await _store.UpdateAsync(session, ct);
        }
        catch (DuplicateNameException)
        {
            // Lost a race against a concurrent join with the same name.
            return JoinResult.NameTaken();
        }

        return JoinResult.Ok(ToSnapshot(session), ToInfo(participant, session.State == SessionState.Revealed));
    }

    public async Task<LeaveResult> LeaveAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var session = await _store.FindByShortCodeAsync(shortCode, ct);
        if (session is null)
        {
            return LeaveResult.NotFound();
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant is null)
        {
            return LeaveResult.NotInSession();
        }

        session.Participants.Remove(participant);

        // If the organiser intentionally leaves, the session falls back to the no-organiser rule
        // (any participant may reveal/reset) rather than being stuck. See #39.
        if (session.OrganiserUserId == userId)
        {
            session.OrganiserUserId = null;
        }

        session.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);

        return LeaveResult.Ok(ToSnapshot(session));
    }

    /// <summary>
    /// Marks a participant as disconnected without removing them, so a reconnect can reclaim their
    /// seat (vote/role/organiser). Idle eviction removes them if they stay away. See #34.
    /// </summary>
    public async Task<SessionActionResult> MarkDisconnectedAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var session = await _store.FindByShortCodeAsync(shortCode, ct);
        if (session is null)
        {
            return SessionActionResult.NotFound();
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant is null)
        {
            return SessionActionResult.NotParticipant();
        }

        participant.IsConnected = false;
        participant.LastSeenAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);
        return SessionActionResult.Ok(ToSnapshot(session));
    }

    // --- Voting & reveal ---------------------------------------------------

    /// <summary>Sets the caller's own vote. Observers are rejected; the card must be in the deck.</summary>
    public async Task<SessionActionResult> CastVoteAsync(string shortCode, string userId, string card, CancellationToken ct = default)
    {
        var session = await _store.FindByShortCodeAsync(shortCode, ct);
        if (session is null)
        {
            return SessionActionResult.NotFound();
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant is null)
        {
            return SessionActionResult.NotParticipant();
        }

        if (session.ClosedAt is not null)
        {
            return SessionActionResult.SessionClosed(); // read-only (#26)
        }

        if (participant.Role == ParticipantRole.Observer)
        {
            return SessionActionResult.ObserverCannotVote();
        }

        if (!DeckCatalog.GetCards(session.DeckType, session.CustomCards).Contains(card))
        {
            return SessionActionResult.InvalidCard();
        }

        participant.Vote = card;
        participant.HasVoted = true;

        if (session.State == SessionState.Revealed)
        {
            // Re-estimating during the discussion — flag the changed card. See #23.
            participant.ChangedAfterReveal = true;
        }
        else
        {
            MaybeAutoReveal(session);
        }

        return await CommitAsync(session, ct);
    }

    public async Task<SessionActionResult> RevealAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        session!.State = SessionState.Revealed;
        StopRunningTimer(session); // the round is over — stop any countdown
        return await CommitAsync(session, ct);
    }

    /// <summary>Clears every vote and starts a fresh round. The auto-reveal flag and story persist.</summary>
    public async Task<SessionActionResult> ResetRoundAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        foreach (var p in session!.Participants)
        {
            ClearVote(p);
        }

        session.State = SessionState.Voting;
        StopRunningTimer(session); // a fresh round starts with no countdown until the organiser starts one
        return await CommitAsync(session, ct);
    }

    /// <summary>Clears a single participant's vote so they can re-cast (used during discussion).</summary>
    public async Task<SessionActionResult> ResetVoteAsync(string shortCode, string userId, string targetUserId, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var target = session!.Participants.FirstOrDefault(p => p.UserId == targetUserId);
        if (target is null)
        {
            return SessionActionResult.TargetNotFound();
        }

        ClearVote(target);
        return await CommitAsync(session, ct);
    }

    public async Task<SessionActionResult> SetAutoRevealAsync(string shortCode, string userId, bool enabled, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        session!.AutoReveal = enabled;
        if (enabled)
        {
            // Turning it on with all votes already in reveals immediately. See #18.
            MaybeAutoReveal(session);
        }

        return await CommitAsync(session, ct);
    }

    /// <summary>Organiser-only: allow or forbid participants changing their own role mid-session. See #21.</summary>
    public async Task<SessionActionResult> SetAllowRoleChangeAsync(string shortCode, string userId, bool enabled, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        session!.AllowRoleChange = enabled;
        return await CommitAsync(session, ct);
    }

    /// <summary>
    /// Switch a participant's Voter ↔ Observer role mid-session (#21). The organiser may change anyone;
    /// a participant may change **their own** role only when <see cref="Models.Session.AllowRoleChange"/>
    /// is on. Switching to Observer clears any held vote; a switch can complete the auto-reveal gate.
    /// </summary>
    public async Task<SessionActionResult> ChangeRoleAsync(string shortCode, string actingUserId, string targetUserId, ParticipantRole role, CancellationToken ct = default)
    {
        var session = await _store.FindByShortCodeAsync(shortCode, ct);
        if (session is null)
        {
            return SessionActionResult.NotFound();
        }

        if (session.Participants.All(p => p.UserId != actingUserId))
        {
            return SessionActionResult.NotParticipant();
        }

        if (session.ClosedAt is not null)
        {
            return SessionActionResult.SessionClosed(); // read-only (#26)
        }

        var isOrganiser = CanControl(session, actingUserId);
        var changingSomeoneElse = targetUserId != actingUserId;

        // Only the organiser may change another participant's role.
        if (changingSomeoneElse && !isOrganiser)
        {
            return SessionActionResult.NotOrganiser();
        }

        // A non-organiser changing their own role needs the organiser to have it enabled.
        if (!isOrganiser && !session.AllowRoleChange)
        {
            return SessionActionResult.RoleChangeDisabled();
        }

        var target = session.Participants.FirstOrDefault(p => p.UserId == targetUserId);
        if (target is null)
        {
            return SessionActionResult.TargetNotFound();
        }

        target.Role = role;
        if (role == ParticipantRole.Observer)
        {
            // An observer holds no vote.
            ClearVote(target);
        }

        // The voter set changed — a pending round may now be complete (e.g. the last unvoted voter
        // became an observer). Re-evaluate the auto-reveal gate. See #18.
        MaybeAutoReveal(session);

        return await CommitAsync(session, ct);
    }

    /// <summary>Organiser-only: enable or disable ephemeral emoji reactions for the session. See #17.</summary>
    public async Task<SessionActionResult> SetReactionsEnabledAsync(string shortCode, string userId, bool enabled, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        session!.ReactionsEnabled = enabled;
        return await CommitAsync(session, ct);
    }

    /// <summary>
    /// Whether reactions are currently enabled for a session. Used by the hub to enforce the toggle
    /// server-side (a client must not be able to broadcast a reaction when the organiser turned it
    /// off). Returns false for an unknown session. See #17.
    /// </summary>
    public Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken ct = default) =>
        // Projected check in the store — must not load the session aggregate on the per-reaction hot path.
        _store.AreReactionsEnabledAsync(shortCode, ct);

    // --- Round timer (organiser-controlled, #14) ---------------------------

    /// <summary>Bounds for a round timer (seconds): at least 5s, at most one hour.</summary>
    public const int MinTimerSeconds = 5;
    public const int MaxTimerSeconds = 3600;

    /// <summary>
    /// Organiser-only: set or clear (null) the configured round-timer duration without starting it.
    /// This is the "changeable mid-session" control; it also seeds restarts. See #14.
    /// </summary>
    public async Task<SessionActionResult> SetTimerDurationAsync(string shortCode, string userId, int? seconds, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        session!.TimerDurationSeconds = NormalizeTimerDuration(seconds);
        return await CommitAsync(session, ct);
    }

    /// <summary>
    /// Organiser-only: start (or restart) the round timer. Uses <paramref name="seconds"/> when given
    /// (also updating the configured duration), otherwise the configured duration. No-op-ish if neither
    /// is available. Clears any paused state. See #14.
    /// </summary>
    public async Task<SessionActionResult> StartTimerAsync(string shortCode, string userId, int? seconds = null, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var duration = NormalizeTimerDuration(seconds) ?? session!.TimerDurationSeconds;
        if (duration is null)
        {
            // Nothing to start with — leave the timer untouched.
            return SessionActionResult.Ok(ToSnapshot(session!));
        }

        session!.TimerDurationSeconds = duration;
        session.TimerPausedRemainingSeconds = null;
        session.TimerDeadline = _clock.UtcNow.AddSeconds(duration.Value);
        return await CommitAsync(session, ct);
    }

    /// <summary>Organiser-only: pause a running timer, freezing the seconds left. See #14.</summary>
    public async Task<SessionActionResult> PauseTimerAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        if (session!.TimerDeadline is { } deadline)
        {
            var remaining = (int)Math.Ceiling((deadline - _clock.UtcNow).TotalSeconds);
            session.TimerPausedRemainingSeconds = Math.Max(0, remaining);
            session.TimerDeadline = null;
        }

        return await CommitAsync(session, ct);
    }

    /// <summary>Organiser-only: resume a paused timer from the frozen remaining time. See #14.</summary>
    public async Task<SessionActionResult> ResumeTimerAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        if (session!.TimerPausedRemainingSeconds is { } remaining)
        {
            session.TimerDeadline = _clock.UtcNow.AddSeconds(Math.Max(0, remaining));
            session.TimerPausedRemainingSeconds = null;
        }

        return await CommitAsync(session, ct);
    }

    /// <summary>Organiser-only: stop/cancel the timer (back to idle); the configured duration is kept. See #14.</summary>
    public async Task<SessionActionResult> StopTimerAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        StopRunningTimer(session!);
        return await CommitAsync(session!, ct);
    }

    // --- Lifecycle: close (read-only) & delete (soft) — organiser-only (#26) ---

    /// <summary>
    /// Organiser-only: close the session into a frozen **read-only** state (#26). Still viewable, but
    /// every mutation is rejected afterwards. Idempotent; stops any running timer so it can't auto-reveal
    /// a frozen session.
    /// </summary>
    public async Task<SessionActionResult> CloseSessionAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct, allowClosed: true);
        if (error is not null)
        {
            return error;
        }

        if (session!.ClosedAt is null)
        {
            session.ClosedAt = _clock.UtcNow;
            StopRunningTimer(session);
        }

        return await CommitAsync(session, ct);
    }

    /// <summary>
    /// Organiser-only: **soft-delete** the session (#26) — sets <see cref="Session.DeletedAt"/>, after
    /// which the global query filter hides it from every read (can't be viewed or joined). The row is
    /// retained. Allowed even when the session is closed. Returns Ok with no snapshot (it's gone).
    /// </summary>
    public async Task<SessionActionResult> DeleteSessionAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct, allowClosed: true);
        if (error is not null)
        {
            return error;
        }

        session!.DeletedAt = _clock.UtcNow;
        session.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);
        // Ok with no snapshot — the session is gone; the hub broadcasts "SessionClosed" instead.
        return new SessionActionResult(SessionActionStatus.Ok, null);
    }

    /// <summary>Clamps a requested timer duration into the allowed range; null stays null (no timer).</summary>
    private static int? NormalizeTimerDuration(int? seconds) =>
        seconds is null ? null : Math.Clamp(seconds.Value, MinTimerSeconds, MaxTimerSeconds);

    /// <summary>Clears the running/paused timer state (idle), leaving the configured duration intact.</summary>
    private static void StopRunningTimer(Session session)
    {
        session.TimerDeadline = null;
        session.TimerPausedRemainingSeconds = null;
    }

    /// <summary>
    /// Organiser-only: swap the active deck mid-session (#11). Validates via <see cref="DeckCatalog"/>;
    /// because prior cards may no longer be valid, this **resets the round** (clears every vote, back to
    /// Voting) and stops any running timer. The new deck rides the broadcast snapshot.
    /// </summary>
    public async Task<SessionActionResult> SetDeckAsync(string shortCode, string userId, DeckType deckType, string? customCards, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
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

        session!.DeckType = deckType;
        session.CustomCards = deckType == DeckType.Custom ? customCards : null;

        // Prior votes were cast against the old deck — clear them and start a fresh round.
        foreach (var p in session.Participants)
        {
            ClearVote(p);
        }
        session.State = SessionState.Voting;
        StopRunningTimer(session);

        return await CommitAsync(session, ct);
    }

    public async Task<SessionActionResult> SetStoryAsync(string shortCode, string userId, string? title, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        session!.CurrentStory = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        return await CommitAsync(session, ct);
    }

    // --- Helpers -----------------------------------------------------------

    /// <summary>Loads a session and verifies the caller may control it (organiser, or anyone if none).</summary>
    private async Task<(Session? Session, SessionActionResult? Error)> LoadForControlAsync(
        string shortCode, string userId, CancellationToken ct, bool allowClosed = false)
    {
        var session = await _store.FindByShortCodeAsync(shortCode, ct);
        if (session is null)
        {
            return (null, SessionActionResult.NotFound());
        }

        if (session.Participants.All(p => p.UserId != userId))
        {
            return (null, SessionActionResult.NotParticipant());
        }

        if (!CanControl(session, userId))
        {
            return (null, SessionActionResult.NotOrganiser());
        }

        // A closed session is read-only (#26): block normal mutations. Close/Delete pass allowClosed.
        if (!allowClosed && session.ClosedAt is not null)
        {
            return (null, SessionActionResult.SessionClosed());
        }

        return (session, null);
    }

    /// <summary>True if the user may reveal/reset: the organiser, or anyone when the session has none.</summary>
    private static bool CanControl(Session session, string userId) =>
        session.OrganiserUserId is null || session.OrganiserUserId == userId;

    /// <summary>Auto-reveal fires only while voting, when enabled, and once every voter has voted.</summary>
    private static void MaybeAutoReveal(Session session)
    {
        if (session.State != SessionState.Voting || !session.AutoReveal)
        {
            return;
        }

        // Only connected voters gate the reveal — a voter who dropped mid-round shouldn't block it.
        var voters = session.Participants
            .Where(p => p.Role == ParticipantRole.Voter && p.IsConnected)
            .ToList();
        if (voters.Count > 0 && voters.All(p => p.HasVoted))
        {
            session.State = SessionState.Revealed;
            StopRunningTimer(session); // everyone voted early — drop the countdown
        }
    }

    private static void ClearVote(Participant p)
    {
        p.Vote = null;
        p.HasVoted = false;
        p.ChangedAfterReveal = false;
    }

    private async Task<SessionActionResult> CommitAsync(Session session, CancellationToken ct)
    {
        session.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(session, ct);
        return SessionActionResult.Ok(ToSnapshot(session));
    }

    public async Task<SessionSnapshot?> GetByShortCodeAsync(string shortCode, CancellationToken ct = default)
    {
        var session = await _store.FindByShortCodeAsync(shortCode, ct);
        return session is null ? null : ToSnapshot(session);
    }

    /// <summary>Landing info for the /join page — exposes only whether a password is required. See #2.</summary>
    public async Task<SessionLanding?> GetLandingAsync(string shortCode, CancellationToken ct = default)
    {
        var session = await _store.FindByShortCodeAsync(shortCode, ct);
        return session is null ? null : new SessionLanding(session.Name, session.ShortCode, session.PasswordHash is not null);
    }

    /// <summary>Organiser-only: set, change, or clear (null/blank) the session join password. See #2.</summary>
    public async Task<SessionActionResult> SetPasswordAsync(string shortCode, string userId, string? newPassword, CancellationToken ct = default)
    {
        var (session, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        session!.PasswordHash = string.IsNullOrEmpty(newPassword) ? null : _passwordHasher.Hash(newPassword);
        return await CommitAsync(session, ct);
    }

    private async Task<string> GenerateUniqueShortCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxShortCodeAttempts; attempt++)
        {
            var code = _shortCodes.Generate();
            if (!await _store.ShortCodeExistsAsync(code, ct))
            {
                return code;
            }
        }

        // Extremely unlikely; fall back to a guaranteed-unique suffix.
        return $"{_shortCodes.Generate()}-{Guid.NewGuid():N}".ToLowerInvariant()[..24];
    }

    public static SessionSnapshot ToSnapshot(Session session)
    {
        var revealed = session.State == SessionState.Revealed;
        var stats = revealed ? StatsCalculator.Compute(session.Participants) : null;
        var outliers = stats?.OutlierValues ?? Array.Empty<string>();

        return new SessionSnapshot(
            session.Id,
            session.ShortCode,
            session.Name,
            session.DeckType,
            DeckCatalog.GetCards(session.DeckType, session.CustomCards),
            session.State,
            session.OrganiserUserId,
            session.AutoReveal,
            session.ReactionsEnabled,
            session.AllowRoleChange,
            session.ClosedAt is not null,
            session.CurrentStory,
            session.Participants
                .OrderBy(p => p.Id)
                .Select(p => ToInfo(p, revealed, outliers))
                .ToArray(),
            stats,
            ToIntegrationInfo(session),
            session.TimerDurationSeconds,
            session.TimerDeadline,
            session.TimerPausedRemainingSeconds);
    }

    /// <summary>Broadcast-safe integration state (provider + linked issue); null when none. See #4.</summary>
    public static IntegrationInfo? ToIntegrationInfo(Session session)
    {
        if (session.LinkedProvider is not { } provider)
        {
            return null;
        }

        var issue = session.LinkedIssue is { } li
            ? new LinkedIssueInfo(li.Key, li.Title, li.Description, li.Url, li.StoryPoints, li.StoryPointsFieldAvailable)
            : null;

        var selectedKey = session.LinkedIssue?.Key;
        var queue = session.TicketQueue
            .Select(q => new QueuedTicketInfo(q.Key, q.Title, q.Status, q.StoryPoints, q.Url,
                string.Equals(q.Key, selectedKey, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return new IntegrationInfo(provider, issue, queue);
    }

    /// <summary>
    /// Projects a participant. The vote value and outlier flag are only meaningful when revealed;
    /// <paramref name="outlierValues"/> is the set of card values flagged as outliers (#44).
    /// </summary>
    public static ParticipantInfo ToInfo(Participant p, bool revealed, IReadOnlyList<string>? outlierValues = null) => new(
        p.UserId,
        p.DisplayName,
        p.IsOrganiser,
        p.Role,
        p.HasVoted,
        p.ChangedAfterReveal,
        revealed ? p.Vote : null,
        p.IsConnected,
        revealed && p.Vote is not null && (outlierValues?.Contains(p.Vote) ?? false));
}
