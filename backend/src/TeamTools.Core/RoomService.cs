using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Security;

namespace TeamTools.Core;

/// <summary>
/// Room-level outcome of a mutation: the status plus the mutated <see cref="Room"/> so the calling
/// tool service can project its own snapshot. Reuses <see cref="SessionActionStatus"/> because every
/// value it carries is room-level (not found / not a participant / not organiser / closed / …), and
/// the client-visible status names must not change (#19).
/// </summary>
public record RoomOutcome(SessionActionStatus Status, Room? Room)
{
    public static RoomOutcome Ok(Room room) => new(SessionActionStatus.Ok, room);
    public static RoomOutcome NotFound() => new(SessionActionStatus.SessionNotFound, null);
    public static RoomOutcome NotParticipant() => new(SessionActionStatus.NotParticipant, null);
    public static RoomOutcome NotOrganiser() => new(SessionActionStatus.NotOrganiser, null);
    public static RoomOutcome TargetNotFound() => new(SessionActionStatus.TargetNotFound, null);
    public static RoomOutcome RoleChangeDisabled() => new(SessionActionStatus.RoleChangeDisabled, null);
    public static RoomOutcome Closed() => new(SessionActionStatus.SessionClosed, null);
}

/// <summary>Room-level outcome of a join, carrying the joined/reclaimed seat.</summary>
public record RoomJoinOutcome(JoinStatus Status, Room? Room, Participant? Participant, string? Error)
{
    public static RoomJoinOutcome Ok(Room room, Participant participant) =>
        new(JoinStatus.Ok, room, participant, null);
}

/// <summary>
/// The room engine (#19): everything true of any ceremony room — creation of the room shell,
/// join with per-room name uniqueness and reconnect reclaim, leave, presence, roles, the organiser
/// set and succession, the join password, and the close/soft-delete lifecycle.
/// <para>
/// Deliberately knows nothing about decks, votes, cards or phases. Tool services (PokerService,
/// RetroService) call it for room concerns and project their own snapshots from the returned
/// <see cref="Room"/>. Pure of transport/EF concerns, so it is unit-testable against an in-memory
/// <see cref="IRoomStore"/>.
/// </para>
/// <para>
/// <b>The tool hook.</b> Some room-level changes alter a tool's completion gate — making someone an
/// observer can complete a poker round, for example. Rather than teach the room engine about
/// rounds, the affected methods take an <c>afterChange</c> callback that the tool service supplies
/// (poker passes its auto-reveal gate). It runs after the mutation and before the commit.
/// </para>
/// </summary>
public class RoomService
{
    private const int MaxShortCodeAttempts = 10;

    private readonly IRoomStore _store;
    private readonly IShortCodeGenerator _shortCodes;
    private readonly IClock _clock;
    private readonly IPasswordHasher _passwordHasher;
    private readonly SessionLimits _limits;

    public RoomService(
        IRoomStore store, IShortCodeGenerator shortCodes, IClock clock,
        IPasswordHasher? passwordHasher = null, SessionLimits? limits = null)
    {
        _store = store;
        _shortCodes = shortCodes;
        _clock = clock;
        _passwordHasher = passwordHasher ?? new Pbkdf2PasswordHasher();
        _limits = limits ?? new SessionLimits();
    }

    internal IClock Clock => _clock;

    // --- Creation ----------------------------------------------------------

    /// <summary>
    /// Builds (but does not persist) a new room shell with its creator seated. The tool service
    /// attaches its payload and saves, so a room and its payload are created in one write. See #19.
    /// </summary>
    public async Task<Room> NewRoomAsync(
        RoomTool tool, string name, string creatorUserId, string creatorDisplayName, bool organise,
        bool enableReactions, string? password, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var room = new Room
        {
            Id = Guid.NewGuid(),
            ShortCode = await GenerateUniqueShortCodeAsync(ct),
            Name = name.Trim(),
            Tool = tool,
            OrganiserUserId = organise ? creatorUserId : null,
            ReactionsEnabled = enableReactions,
            // Hash an optional join password; the plaintext is never stored. See #2.
            PasswordHash = string.IsNullOrEmpty(password) ? null : _passwordHasher.Hash(password),
            CreatedAt = now,
            LastActivityAt = now,
        };

        room.Participants.Add(new Participant
        {
            RoomId = room.Id,
            UserId = creatorUserId,
            DisplayName = creatorDisplayName.Trim(),
            NormalizedName = NameNormalizer.Normalize(creatorDisplayName),
            IsOrganiser = organise,
            // An organiser runs the room rather than taking part, so they default to Observer.
            // A non-organising creator is a normal voter. See #10.
            Role = organise ? ParticipantRole.Observer : ParticipantRole.Voter,
            IsConnected = true,
            LastSeenAt = now,
        });

        return room;
    }

    public Task AddAsync(Room room, CancellationToken ct = default) => _store.AddAsync(room, ct);

    // --- Join / leave / presence -------------------------------------------

    /// <summary>
    /// Seats a participant: enforces the password gate, per-room name uniqueness and the participant
    /// cap, and reclaims an existing seat on reconnect (keeping vote, role and organiser rights).
    /// <para>
    /// <paramref name="expectedTool"/> is the tool of the service asking. It is optional only so a
    /// tool-agnostic caller can omit it; both tool services pass their own, which is what stops one
    /// tool seating a participant in the other's room (#32).
    /// </para>
    /// </summary>
    public async Task<RoomJoinOutcome> JoinAsync(
        JoinSessionRequest request, RoomTool? expectedTool = null, CancellationToken ct = default)
    {
        if (NameNormalizer.IsBlank(request.DisplayName))
        {
            return new RoomJoinOutcome(JoinStatus.InvalidName, null, null, "A display name is required.");
        }

        var room = await _store.FindByShortCodeAsync(request.ShortCode, ct);
        if (room is null)
        {
            return new RoomJoinOutcome(JoinStatus.SessionNotFound, null, null, "Session not found.");
        }

        // The short code names a room, and a room hosts exactly one tool (#19). A tool service must
        // not seat anyone in a room it does not host — before #32 the poker service would add the
        // participant and only then fail projecting a poker snapshot for a retro room, leaving a
        // seat behind and reporting a server error (#32).
        if (expectedTool is { } tool && room.Tool != tool)
        {
            return new RoomJoinOutcome(
                JoinStatus.WrongTool, null, null,
                $"That code is a {room.Tool} room, not a {tool} one.");
        }

        var normalized = NameNormalizer.Normalize(request.DisplayName);
        var existing = room.Participants.FirstOrDefault(p => p.UserId == request.UserId);

        // A closed room is read-only (#26): no new joiners. An already-present participant may still
        // reconnect to view the frozen result.
        if (room.ClosedAt is not null && existing is null)
        {
            return new RoomJoinOutcome(
                JoinStatus.SessionClosed, null, null,
                "This session is closed and can no longer be joined.");
        }

        // Password gate (#2): a new joiner must supply the correct password. An already-present
        // participant (e.g. reconnecting / auto-rejoin) is past the gate and isn't re-challenged.
        if (room.PasswordHash is { } hash && existing is null)
        {
            if (string.IsNullOrEmpty(request.Password))
            {
                return new RoomJoinOutcome(
                    JoinStatus.PasswordRequired, null, null, "This session requires a password.");
            }
            if (!_passwordHasher.Verify(hash, request.Password))
            {
                return new RoomJoinOutcome(
                    JoinStatus.WrongPassword, null, null, "Incorrect password — please try again.");
            }
        }

        // Reject if the name is used by a DIFFERENT participant (case-insensitive). The caller
        // re-joining with their own userId is allowed to keep their name. See #7.
        var nameClash = room.Participants.Any(p =>
            p.NormalizedName == normalized && p.UserId != request.UserId);
        if (nameClash)
        {
            return NameTaken();
        }

        // Cap room size to guard against abuse (anonymous + public). A reconnecting participant
        // (existing seat) is never blocked — only genuinely new joiners count against the cap. See #3-abuse.
        if (existing is null && room.Participants.Count >= _limits.MaxParticipants)
        {
            return new RoomJoinOutcome(
                JoinStatus.SessionFull, null, null,
                "This session is full — it has reached its participant limit.");
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
                RoomId = room.Id,
                UserId = request.UserId,
                DisplayName = request.DisplayName.Trim(),
                NormalizedName = normalized,
                Role = request.Role,
            };
            room.Participants.Add(participant);
        }

        participant.IsConnected = true;
        participant.LastSeenAt = now;
        // Preserve any organiser rights the participant already holds (so a promoted co-organiser keeps
        // them across a reconnect, #7) and let the founding organiser reclaim theirs even if their row was
        // evicted while away (#34). A brand-new, non-founding participant stays a regular member.
        participant.IsOrganiser = participant.IsOrganiser || room.OrganiserUserId == request.UserId;

        room.LastActivityAt = now;

        try
        {
            await _store.UpdateAsync(room, ct);
        }
        catch (DuplicateNameException)
        {
            // Lost a race against a concurrent join with the same name.
            return NameTaken();
        }

        return RoomJoinOutcome.Ok(room, participant);

        static RoomJoinOutcome NameTaken() => new(
            JoinStatus.NameTaken, null, null,
            "That name is already taken in this session — please pick another.");
    }

    /// <summary>Removes a participant's seat entirely (an intentional leave, not a drop).</summary>
    public async Task<(LeaveStatus Status, Room? Room)> LeaveAsync(
        string shortCode, string userId, CancellationToken ct = default)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        if (room is null)
        {
            return (LeaveStatus.SessionNotFound, null);
        }

        var participant = room.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant is null)
        {
            return (LeaveStatus.NotInSession, null);
        }

        room.Participants.Remove(participant);

        // If the organiser intentionally leaves, the room falls back to the no-organiser rule
        // (any participant may control) rather than being stuck. See #39.
        if (room.OrganiserUserId == userId)
        {
            room.OrganiserUserId = null;
        }

        room.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(room, ct);

        return (LeaveStatus.Ok, room);
    }

    /// <summary>
    /// Marks a participant as disconnected without removing them, so a reconnect can reclaim their
    /// seat (vote/role/organiser). Idle eviction removes them if they stay away. See #34.
    /// </summary>
    public async Task<RoomOutcome> MarkDisconnectedAsync(
        string shortCode, string userId, CancellationToken ct = default)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        if (room is null)
        {
            return RoomOutcome.NotFound();
        }

        var participant = room.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant is null)
        {
            return RoomOutcome.NotParticipant();
        }

        participant.IsConnected = false;
        participant.LastSeenAt = _clock.UtcNow;
        // If that drop left the room with no connected organiser, hand the facilitator role to the
        // longest-present connected participant so it isn't stuck. See #7.
        RoomAuthz.PromoteSuccessorIfNeeded(room);
        await _store.UpdateAsync(room, ct);
        return RoomOutcome.Ok(room);
    }

    // --- Roles & organisers ------------------------------------------------

    /// <summary>
    /// Switch a participant's Voter ↔ Observer role mid-room (#21). An organiser may change anyone;
    /// a participant may change **their own** role only when <see cref="Room.AllowRoleChange"/> is on.
    /// Switching to Observer clears any held vote via <paramref name="onBecameObserver"/>, and the
    /// changed participant set may complete the tool's gate via <paramref name="afterChange"/>.
    /// </summary>
    public async Task<RoomOutcome> ChangeRoleAsync(
        string shortCode, string actingUserId, string targetUserId, ParticipantRole role,
        Action<Participant>? onBecameObserver = null, Action<Room>? afterChange = null,
        CancellationToken ct = default)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        if (room is null)
        {
            return RoomOutcome.NotFound();
        }

        if (room.Participants.All(p => p.UserId != actingUserId))
        {
            return RoomOutcome.NotParticipant();
        }

        if (room.ClosedAt is not null)
        {
            return RoomOutcome.Closed(); // read-only (#26)
        }

        var isOrganiser = RoomAuthz.CanControl(room, actingUserId);
        var changingSomeoneElse = targetUserId != actingUserId;

        // Only the organiser may change another participant's role.
        if (changingSomeoneElse && !isOrganiser)
        {
            return RoomOutcome.NotOrganiser();
        }

        // A non-organiser changing their own role needs the organiser to have it enabled.
        if (!isOrganiser && !room.AllowRoleChange)
        {
            return RoomOutcome.RoleChangeDisabled();
        }

        var target = room.Participants.FirstOrDefault(p => p.UserId == targetUserId);
        if (target is null)
        {
            return RoomOutcome.TargetNotFound();
        }

        target.Role = role;
        if (role == ParticipantRole.Observer)
        {
            // An observer holds no vote.
            onBecameObserver?.Invoke(target);
        }

        return await CommitAsync(room, afterChange, ct);
    }

    /// <summary>Organiser-only: allow or forbid participants changing their own role mid-room. See #21.</summary>
    public Task<RoomOutcome> SetAllowRoleChangeAsync(
        string shortCode, string userId, bool enabled, CancellationToken ct = default) =>
        MutateAsync(shortCode, userId, room => room.AllowRoleChange = enabled, ct);

    /// <summary>Organiser-only: enable or disable ephemeral emoji reactions for the room. See #17.</summary>
    public Task<RoomOutcome> SetReactionsEnabledAsync(
        string shortCode, string userId, bool enabled, CancellationToken ct = default) =>
        MutateAsync(shortCode, userId, room => room.ReactionsEnabled = enabled, ct);

    /// <summary>
    /// Whether reactions are currently enabled for a room. Used by the hub to enforce the toggle
    /// server-side (a client must not be able to broadcast a reaction when the organiser turned it
    /// off). Returns false for an unknown room. See #17.
    /// </summary>
    public Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken ct = default) =>
        // Projected check in the store — must not load the room aggregate on the per-reaction hot path.
        _store.AreReactionsEnabledAsync(shortCode, ct);

    /// <summary>Organiser-only: grant a participant co-organiser rights. See #7.</summary>
    public Task<RoomOutcome> PromoteToOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        MutateTargetAsync(shortCode, actingUserId, targetUserId, (_, target) => target.IsOrganiser = true, ct);

    /// <summary>
    /// Organiser-only: revoke a participant's organiser rights. If the founding organiser is demoted,
    /// the founding pointer is cleared so it doesn't silently re-grant control. Demoting the last
    /// organiser is allowed — the room simply reverts to the open "anyone controls" rule (#10).
    /// </summary>
    public Task<RoomOutcome> DemoteOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        MutateTargetAsync(shortCode, actingUserId, targetUserId, (room, target) =>
        {
            target.IsOrganiser = false;
            if (room.OrganiserUserId == targetUserId)
            {
                room.OrganiserUserId = null;
            }
        }, ct);

    /// <summary>
    /// Organiser-only: hand off facilitation — promote the target and step down in one move. The
    /// founding pointer moves with it so the new organiser survives an eviction-reclaim. See #7.
    /// </summary>
    public async Task<RoomOutcome> TransferOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default)
    {
        var (room, error) = await LoadForControlAsync(shortCode, actingUserId, ct);
        if (error is not null)
        {
            return error;
        }

        var target = room!.Participants.FirstOrDefault(p => p.UserId == targetUserId);
        if (target is null)
        {
            return RoomOutcome.TargetNotFound();
        }

        if (targetUserId == actingUserId)
        {
            return RoomOutcome.Ok(room); // handing off to yourself is a no-op
        }

        target.IsOrganiser = true;
        var self = room.Participants.First(p => p.UserId == actingUserId);
        self.IsOrganiser = false;
        if (room.OrganiserUserId == actingUserId)
        {
            room.OrganiserUserId = targetUserId;
        }

        return await CommitAsync(room, null, ct);
    }

    // --- Password & lifecycle ----------------------------------------------

    /// <summary>
    /// Whether <paramref name="password"/> opens this room. A room with no password is open, so an
    /// absent password is the correct answer for one.
    /// <para>
    /// This lives on the room engine because the password is a <em>room</em> property: the join gate
    /// (#2), the retro export (#28) and the poker export (#30) all have to reach the same verdict,
    /// and a tool service holding its own hasher is how two of them would eventually stop agreeing.
    /// </para>
    /// </summary>
    public bool VerifyPassword(Room room, string? password) =>
        room.PasswordHash is not { } hash || _passwordHasher.Verify(hash, password ?? string.Empty);

    /// <summary>Organiser-only: set, change, or clear (null/blank) the room join password. See #2.</summary>
    public Task<RoomOutcome> SetPasswordAsync(
        string shortCode, string userId, string? newPassword, CancellationToken ct = default) =>
        MutateAsync(shortCode, userId, room =>
            room.PasswordHash = string.IsNullOrEmpty(newPassword) ? null : _passwordHasher.Hash(newPassword), ct);

    /// <summary>
    /// Organiser-only: close the room into a frozen **read-only** state (#26). Still viewable, but
    /// every mutation is rejected afterwards. Idempotent. <paramref name="onClosed"/> lets the tool
    /// stop anything time-based (poker: a running timer, so it can't auto-reveal a frozen room).
    /// </summary>
    public async Task<RoomOutcome> CloseRoomAsync(
        string shortCode, string userId, Action<Room>? onClosed = null, CancellationToken ct = default)
    {
        var (room, error) = await LoadForControlAsync(shortCode, userId, ct, allowClosed: true);
        if (error is not null)
        {
            return error;
        }

        if (room!.ClosedAt is null)
        {
            room.ClosedAt = _clock.UtcNow;
            onClosed?.Invoke(room);
        }

        return await CommitAsync(room, null, ct);
    }

    /// <summary>
    /// Organiser-only: **soft-delete** the room (#26) — sets <see cref="Room.DeletedAt"/>, after which
    /// the global query filter hides it from every read (can't be viewed or joined). The row is
    /// retained. Allowed even when the room is closed. Returns Ok with no room (it's gone).
    /// </summary>
    public async Task<RoomOutcome> DeleteRoomAsync(
        string shortCode, string userId, CancellationToken ct = default)
    {
        var (room, error) = await LoadForControlAsync(shortCode, userId, ct, allowClosed: true);
        if (error is not null)
        {
            return error;
        }

        room!.DeletedAt = _clock.UtcNow;
        room.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(room, ct);
        // Ok with no room — it's gone; the hub broadcasts "SessionClosed" instead.
        return new RoomOutcome(SessionActionStatus.Ok, null);
    }

    /// <summary>Landing info for the /join page — exposes only whether a password is required. See #2.</summary>
    public async Task<SessionLanding?> GetLandingAsync(string shortCode, CancellationToken ct = default)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        return room is null
            ? null
            : new SessionLanding(room.Name, room.ShortCode, room.PasswordHash is not null, room.Tool);
    }

    // --- Helpers -----------------------------------------------------------

    /// <summary>Loads a room and verifies the caller may control it (organiser, or anyone if none).</summary>
    public async Task<(Room? Room, RoomOutcome? Error)> LoadForControlAsync(
        string shortCode, string userId, CancellationToken ct, bool allowClosed = false)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        if (room is null)
        {
            return (null, RoomOutcome.NotFound());
        }

        if (room.Participants.All(p => p.UserId != userId))
        {
            return (null, RoomOutcome.NotParticipant());
        }

        if (!RoomAuthz.CanControl(room, userId))
        {
            return (null, RoomOutcome.NotOrganiser());
        }

        // A closed room is read-only (#26): block normal mutations. Close/Delete pass allowClosed.
        if (!allowClosed && room.ClosedAt is not null)
        {
            return (null, RoomOutcome.Closed());
        }

        return (room, null);
    }

    /// <summary>Loads a room and verifies the caller is in it (no organiser requirement).</summary>
    public async Task<(Room? Room, RoomOutcome? Error)> LoadForParticipantAsync(
        string shortCode, string userId, CancellationToken ct)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        if (room is null)
        {
            return (null, RoomOutcome.NotFound());
        }

        if (room.Participants.All(p => p.UserId != userId))
        {
            return (null, RoomOutcome.NotParticipant());
        }

        if (room.ClosedAt is not null)
        {
            return (null, RoomOutcome.Closed());
        }

        return (room, null);
    }

    public Task<Room?> FindByShortCodeAsync(string shortCode, CancellationToken ct = default) =>
        _store.FindByShortCodeAsync(shortCode, ct);

    /// <summary>Stamps activity, runs the tool hook, and persists. See #19.</summary>
    public async Task<RoomOutcome> CommitAsync(Room room, Action<Room>? afterChange, CancellationToken ct)
    {
        afterChange?.Invoke(room);
        room.LastActivityAt = _clock.UtcNow;
        await _store.UpdateAsync(room, ct);
        return RoomOutcome.Ok(room);
    }

    private async Task<RoomOutcome> MutateAsync(
        string shortCode, string userId, Action<Room> mutate, CancellationToken ct)
    {
        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        mutate(room!);
        return await CommitAsync(room!, null, ct);
    }

    private async Task<RoomOutcome> MutateTargetAsync(
        string shortCode, string actingUserId, string targetUserId,
        Action<Room, Participant> mutate, CancellationToken ct)
    {
        var (room, error) = await LoadForControlAsync(shortCode, actingUserId, ct);
        if (error is not null)
        {
            return error;
        }

        var target = room!.Participants.FirstOrDefault(p => p.UserId == targetUserId);
        if (target is null)
        {
            return RoomOutcome.TargetNotFound();
        }

        mutate(room, target);
        return await CommitAsync(room, null, ct);
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
}
