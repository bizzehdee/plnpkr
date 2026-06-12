using Microsoft.AspNetCore.SignalR;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Integrations;
using PlanningPoker.Core.Models;

namespace PlanningPoker.Api.Hubs;

/// <summary>
/// SignalR hub for real-time session interaction. Thin adapter: every method delegates to
/// <see cref="SessionService"/> and broadcasts results to the session's group.
/// One SignalR group per session, keyed by short code.
/// </summary>
public class PlanningPokerHub : Hub
{
    private readonly SessionService _sessions;
    private readonly ConnectionRegistry _connections;
    private readonly IntegrationService _integrations;
    private readonly ReactionRateLimiter _reactions;

    public PlanningPokerHub(
        SessionService sessions, ConnectionRegistry connections, IntegrationService integrations,
        ReactionRateLimiter reactions)
    {
        _sessions = sessions;
        _connections = connections;
        _integrations = integrations;
        _reactions = reactions;
    }

    /// <summary>Liveness handshake (retained from M1).</summary>
    public Task<PingResponse> Ping(string? message) =>
        Task.FromResult(new PingResponse(
            string.IsNullOrWhiteSpace(message) ? "pong" : $"pong: {message}",
            Context.ConnectionId,
            DateTimeOffset.UtcNow));

    public async Task<CreateSessionResult> CreateSession(
        string name, DeckType deckType, string? customCards, string userId, string displayName, bool organise,
        string? password, bool enableReactions, int? timerDurationSeconds)
    {
        var result = await _sessions.CreateAsync(
            new CreateSessionRequest(
                name, deckType, customCards, userId, displayName, organise, password, enableReactions,
                timerDurationSeconds));

        if (result.Status == CreateSessionStatus.Ok)
        {
            await JoinGroupAndTrack(result.Session!.ShortCode, userId);
        }

        return result;
    }

    public async Task<JoinResult> JoinSession(string shortCode, string userId, string displayName, ParticipantRole role, string? password)
    {
        var result = await _sessions.JoinAsync(new JoinSessionRequest(shortCode, userId, displayName, role, password));

        if (result.Status == JoinStatus.Ok)
        {
            await JoinGroupAndTrack(shortCode, userId);
            await BroadcastSessionUpdated(result.Session!);
        }

        return result;
    }

    public Task<SessionActionResult> CastVote(string shortCode, string userId, string card) =>
        MutateAndBroadcast(() => _sessions.CastVoteAsync(shortCode, userId, card));

    public Task<SessionActionResult> RevealVotes(string shortCode, string userId) =>
        MutateAndBroadcast(() => _sessions.RevealAsync(shortCode, userId));

    public Task<SessionActionResult> ResetRound(string shortCode, string userId) =>
        MutateAndBroadcast(() => _sessions.ResetRoundAsync(shortCode, userId));

    public Task<SessionActionResult> ResetVote(string shortCode, string userId, string targetUserId) =>
        MutateAndBroadcast(() => _sessions.ResetVoteAsync(shortCode, userId, targetUserId));

    public Task<SessionActionResult> SetAutoReveal(string shortCode, string userId, bool enabled) =>
        MutateAndBroadcast(() => _sessions.SetAutoRevealAsync(shortCode, userId, enabled));

    public Task<SessionActionResult> SetStory(string shortCode, string userId, string? title) =>
        MutateAndBroadcast(() => _sessions.SetStoryAsync(shortCode, userId, title));

    // Organiser sets/changes/clears the join password. Plaintext stays on this WSS call; only the
    // (password-free) snapshot is broadcast — the hash never leaves the server. See #2.
    public Task<SessionActionResult> SetPassword(string shortCode, string userId, string? password) =>
        MutateAndBroadcast(() => _sessions.SetPasswordAsync(shortCode, userId, password));

    // Organiser swaps the active deck mid-session (#11); resets the round and broadcasts the new deck.
    public Task<SessionActionResult> SetDeck(string shortCode, string userId, DeckType deckType, string? customCards) =>
        MutateAndBroadcast(() => _sessions.SetDeckAsync(shortCode, userId, deckType, customCards));

    // Organiser closes the session into read-only mode (#26); the snapshot (IsClosed=true) is broadcast.
    public Task<SessionActionResult> CloseSession(string shortCode, string userId) =>
        MutateAndBroadcast(() => _sessions.CloseSessionAsync(shortCode, userId));

    // Organiser soft-deletes the session (#26). On success the group is told it's gone (SessionClosed),
    // so every client falls back to the "session has ended" screen; the row is hidden via the query filter.
    public async Task<SessionActionResult> DeleteSession(string shortCode, string userId)
    {
        var result = await _sessions.DeleteSessionAsync(shortCode, userId);
        if (result.Status == SessionActionStatus.Ok)
        {
            await Clients.Group(GroupName(shortCode)).SendAsync("SessionClosed");
        }

        return result;
    }

    // Organiser toggles emoji reactions on/off mid-session; the new state rides the broadcast snapshot. (#17)
    public Task<SessionActionResult> SetReactionsEnabled(string shortCode, string userId, bool enabled) =>
        MutateAndBroadcast(() => _sessions.SetReactionsEnabledAsync(shortCode, userId, enabled));

    // Mid-session role changes (#21): organiser toggles whether participants may self-switch; a
    // participant (or the organiser, for anyone) flips Voter ↔ Observer. New roles ride the snapshot.
    public Task<SessionActionResult> SetAllowRoleChange(string shortCode, string userId, bool enabled) =>
        MutateAndBroadcast(() => _sessions.SetAllowRoleChangeAsync(shortCode, userId, enabled));

    public Task<SessionActionResult> ChangeRole(string shortCode, string userId, string targetUserId, ParticipantRole role) =>
        MutateAndBroadcast(() => _sessions.ChangeRoleAsync(shortCode, userId, targetUserId, role));

    // --- Round timer (organiser-controlled, #14). Each delegates to the service and broadcasts the
    //     new snapshot; clients tick locally against the broadcast deadline. ---

    public Task<SessionActionResult> SetTimerDuration(string shortCode, string userId, int? seconds) =>
        MutateAndBroadcast(() => _sessions.SetTimerDurationAsync(shortCode, userId, seconds));

    public Task<SessionActionResult> StartTimer(string shortCode, string userId, int? seconds) =>
        MutateAndBroadcast(() => _sessions.StartTimerAsync(shortCode, userId, seconds));

    public Task<SessionActionResult> PauseTimer(string shortCode, string userId) =>
        MutateAndBroadcast(() => _sessions.PauseTimerAsync(shortCode, userId));

    public Task<SessionActionResult> ResumeTimer(string shortCode, string userId) =>
        MutateAndBroadcast(() => _sessions.ResumeTimerAsync(shortCode, userId));

    public Task<SessionActionResult> StopTimer(string shortCode, string userId) =>
        MutateAndBroadcast(() => _sessions.StopTimerAsync(shortCode, userId));

    // --- Issue-tracker integration (#4). The token is received here over WSS and handed to the
    //     service; it is never broadcast. Only the safe snapshot (provider + linked issue) is. ---

    public Task<IntegrationResult> ConnectTracker(
        string shortCode, string userId, IntegrationProvider provider, string baseUrl, string? email, string token,
        string? storyPointsField = null) =>
        IntegrationAndBroadcast(() => _integrations.ConnectAsync(shortCode, userId, provider, baseUrl, email, token, storyPointsField));

    public Task<IntegrationResult> DisconnectTracker(string shortCode, string userId) =>
        IntegrationAndBroadcast(() => _integrations.DisconnectAsync(shortCode, userId));

    public Task<IntegrationResult> LinkIssue(string shortCode, string userId, string issueKey) =>
        IntegrationAndBroadcast(() => _integrations.LinkIssueAsync(shortCode, userId, issueKey));

    public Task<IntegrationResult> SubmitStoryPoints(string shortCode, string userId, double points) =>
        IntegrationAndBroadcast(() => _integrations.SubmitStoryPointsAsync(shortCode, userId, points));

    public Task<IntegrationResult> LoadQueueFromUrl(string shortCode, string userId, string url) =>
        IntegrationAndBroadcast(() => _integrations.LoadQueueFromUrlAsync(shortCode, userId, url));

    public Task<IntegrationResult> LoadQueueFromKeys(string shortCode, string userId, string[] keys) =>
        IntegrationAndBroadcast(() => _integrations.LoadQueueFromKeysAsync(shortCode, userId, keys));

    // Selecting a queued ticket is just linking it (reuses the existing lookup flow, #20).
    public Task<IntegrationResult> SelectQueueItem(string shortCode, string userId, string issueKey) =>
        IntegrationAndBroadcast(() => _integrations.LinkIssueAsync(shortCode, userId, issueKey));

    public Task<IntegrationResult> ClearQueue(string shortCode, string userId) =>
        IntegrationAndBroadcast(() => _integrations.ClearQueueAsync(shortCode, userId));

    private async Task<IntegrationResult> IntegrationAndBroadcast(Func<Task<IntegrationResult>> action)
    {
        var result = await action();
        if (result.Status == IntegrationStatus.Ok && result.Session is not null)
        {
            await BroadcastSessionUpdated(result.Session);
        }

        return result;
    }

    /// <summary>Runs a session mutation and broadcasts the new snapshot to the group when it succeeds.</summary>
    private async Task<SessionActionResult> MutateAndBroadcast(Func<Task<SessionActionResult>> action)
    {
        var result = await action();
        if (result.Status == SessionActionStatus.Ok && result.Session is not null)
        {
            await BroadcastSessionUpdated(result.Session);
        }

        return result;
    }

    // Ephemeral emoji reaction (#17). Not persisted, not in the snapshot — fan-out only. The
    // session/user come from the tracked connection (can't be spoofed); allowlist + per-connection
    // rate limit guard against spam. Silently ignored if disallowed/over-limit/not in a session.
    public async Task React(string emoji)
    {
        if (!ReactionPolicy.IsAllowed(emoji)
            || !_connections.TryGet(Context.ConnectionId, out var info)
            || !_reactions.TryReact(Context.ConnectionId))
        {
            return;
        }

        // Enforce the organiser's toggle server-side — a client must not broadcast while reactions are
        // off (the rate-limit gate above runs first so this DB check can't be spammed). See #17.
        if (!await _sessions.AreReactionsEnabledAsync(info.ShortCode))
        {
            return;
        }

        await Clients.Group(GroupName(info.ShortCode)).SendAsync("ReactionReceived", info.UserId, emoji);
    }

    public async Task LeaveSession(string shortCode, string userId)
    {
        var result = await _sessions.LeaveAsync(shortCode, userId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(shortCode));
        _connections.TryRemove(Context.ConnectionId, out _);

        if (result.Status == LeaveStatus.Ok && result.Session is not null)
        {
            await BroadcastSessionUpdated(result.Session);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _reactions.Forget(Context.ConnectionId);

        // A drop marks the participant away (keeping their vote/role/organiser for reconnect) rather
        // than removing them; idle eviction cleans up if they don't return. See #34.
        if (_connections.TryRemove(Context.ConnectionId, out var info))
        {
            var result = await _sessions.MarkDisconnectedAsync(info.ShortCode, info.UserId);
            if (result.Status == SessionActionStatus.Ok && result.Session is not null)
            {
                await BroadcastSessionUpdated(result.Session);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task JoinGroupAndTrack(string shortCode, string userId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(shortCode));
        _connections.Track(Context.ConnectionId, shortCode, userId);
    }

    private Task BroadcastSessionUpdated(SessionSnapshot snapshot) =>
        Clients.Group(GroupName(snapshot.ShortCode)).SendAsync("SessionUpdated", snapshot);

    /// <summary>The SignalR group name for a session. Shared with the eviction service.</summary>
    public static string GroupName(string shortCode) => $"session:{shortCode}";
}

public record PingResponse(string Reply, string ConnectionId, DateTimeOffset ServerTimeUtc);
