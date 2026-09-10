using Microsoft.AspNetCore.SignalR;
using TeamTools.Core;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Standup;

namespace TeamTools.Api.Hubs;

/// <summary>
/// SignalR hub for Async Standup (#36). Thin adapter over <see cref="StandupService"/>, sharing the
/// room-level connection registry, reaction limiter and abuse throttle with the other three hubs.
/// <para>
/// <b>Broadcasts are per recipient</b>, because post-to-read is a property of the wire: someone who
/// has not posted must not receive everyone else's answers, whatever their client would render.
/// </para>
/// <para>
/// Notice what is absent: no phase methods and no timer. A standup opens, people post, it closes.
/// </para>
/// </summary>
public class StandupHub : Hub
{
    private readonly StandupService _standup;
    private readonly ConnectionRegistry _connections;
    private readonly ReactionRateLimiter _reactions;
    private readonly HubThrottle _throttle;

    public StandupHub(
        StandupService standup, ConnectionRegistry connections,
        ReactionRateLimiter reactions, HubThrottle throttle)
    {
        _standup = standup;
        _connections = connections;
        _reactions = reactions;
        _throttle = throttle;
    }

    // --- Creation & membership ---------------------------------------------

    public async Task<CreateStandupResult> CreateBoard(
        string name, string userId, string displayName, bool organise, string? password,
        bool enableReactions, string[]? questions, string? previousBoardShortCode,
        string? previousBoardPassword)
    {
        if (!_throttle.TryCreate(Context.ConnectionId))
        {
            return CreateStandupResult.RateLimited();
        }

        var result = await _standup.CreateAsync(new CreateStandupRequest(
            name, userId, displayName, organise, password, enableReactions, questions,
            previousBoardShortCode, previousBoardPassword));

        if (result.Status == CreateStandupStatus.Ok)
        {
            await JoinGroupAndTrack(result.Board!.Room.ShortCode, userId);
        }

        return result;
    }

    public async Task<StandupJoinResult> JoinBoard(
        string shortCode, string userId, string displayName, ParticipantRole role, string? password)
    {
        if (!_throttle.TryJoin(Context.ConnectionId))
        {
            return new StandupJoinResult(JoinStatus.RateLimited, null, null,
                "You're doing that too often — please wait a moment and try again.");
        }

        var result = await _standup.JoinAsync(
            new JoinSessionRequest(shortCode, userId, displayName, role, password));

        if (result.Status == JoinStatus.Ok)
        {
            await JoinGroupAndTrack(shortCode, userId);
            await BroadcastBoard(shortCode);
        }

        return result;
    }

    public async Task LeaveBoard(string shortCode, string userId)
    {
        var result = await _standup.LeaveAsync(shortCode, userId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(shortCode));
        _connections.TryRemove(Context.ConnectionId, out _);

        if (result.Status == StandupActionStatus.Ok)
        {
            await BroadcastBoard(shortCode);
        }
    }

    // --- Answering ----------------------------------------------------------

    public async Task<StandupActionResult> Answer(
        string shortCode, string userId, Guid questionId, string text)
    {
        // Answers are the high-frequency write here, as cards are on a retro board (#3-abuse).
        if (!_throttle.TryAddCard(Context.ConnectionId))
        {
            return StandupActionResult.RateLimited();
        }

        return await MutateAndBroadcast(
            shortCode, () => _standup.AnswerAsync(shortCode, userId, questionId, text));
    }

    // --- Blockers -----------------------------------------------------------

    public Task<StandupActionResult> AddBlocker(string shortCode, string userId, string text) =>
        MutateAndBroadcast(shortCode, () => _standup.AddBlockerAsync(shortCode, userId, text));

    public Task<StandupActionResult> AssignBlocker(
        string shortCode, string userId, Guid blockerId, string? ownerUserId, string? ownerName) =>
        MutateAndBroadcast(shortCode, () => _standup.AssignBlockerAsync(
            shortCode, userId, blockerId, ownerUserId, ownerName));

    public Task<StandupActionResult> ToggleBlockerResolved(
        string shortCode, string userId, Guid blockerId) =>
        MutateAndBroadcast(
            shortCode, () => _standup.ToggleBlockerResolvedAsync(shortCode, userId, blockerId));

    public Task<StandupActionResult> DeleteBlocker(string shortCode, string userId, Guid blockerId) =>
        MutateAndBroadcast(shortCode, () => _standup.DeleteBlockerAsync(shortCode, userId, blockerId));

    // --- Room-level operations ---------------------------------------------

    public Task<StandupActionResult> SetReactionsEnabled(
        string shortCode, string userId, bool enabled) =>
        MutateAndBroadcast(
            shortCode, () => _standup.SetReactionsEnabledAsync(shortCode, userId, enabled));

    public Task<StandupActionResult> PromoteToOrganiser(
        string shortCode, string userId, string targetUserId) =>
        MutateAndBroadcast(
            shortCode, () => _standup.PromoteToOrganiserAsync(shortCode, userId, targetUserId));

    public Task<StandupActionResult> TransferOrganiser(
        string shortCode, string userId, string targetUserId) =>
        MutateAndBroadcast(
            shortCode, () => _standup.TransferOrganiserAsync(shortCode, userId, targetUserId));

    public Task<StandupActionResult> SetPassword(string shortCode, string userId, string? password) =>
        MutateAndBroadcast(shortCode, () => _standup.SetPasswordAsync(shortCode, userId, password));

    public Task<StandupActionResult> CloseBoard(string shortCode, string userId) =>
        MutateAndBroadcast(shortCode, () => _standup.CloseBoardAsync(shortCode, userId));

    public async Task<StandupActionResult> DeleteBoard(string shortCode, string userId)
    {
        var result = await _standup.DeleteBoardAsync(shortCode, userId);
        if (result.Status == StandupActionStatus.Ok)
        {
            await Clients.Group(GroupName(shortCode)).SendAsync("BoardClosed");
        }

        return result;
    }

    /// <summary>Ephemeral emoji reaction (#17) — never persisted, rate-limited per connection.</summary>
    public async Task React(string emoji)
    {
        if (!_connections.TryGet(Context.ConnectionId, out var info)
            || !ReactionPolicy.IsAllowed(emoji)
            || !_reactions.TryReact(Context.ConnectionId))
        {
            return;
        }

        if (!await _standup.AreReactionsEnabledAsync(info.ShortCode))
        {
            return;
        }

        await Clients.Group(GroupName(info.ShortCode))
            .SendAsync("ReactionReceived", info.UserId, emoji);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _reactions.Forget(Context.ConnectionId);
        _throttle.Forget(Context.ConnectionId);

        if (_connections.TryRemove(Context.ConnectionId, out var info))
        {
            var result = await _standup.MarkDisconnectedAsync(info.ShortCode, info.UserId);
            if (result.Status == StandupActionStatus.Ok)
            {
                await BroadcastBoard(info.ShortCode);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // --- Plumbing -----------------------------------------------------------

    private async Task<StandupActionResult> MutateAndBroadcast(
        string shortCode, Func<Task<StandupActionResult>> mutate)
    {
        var result = await mutate();
        if (result.Status == StandupActionStatus.Ok)
        {
            await BroadcastBoard(shortCode);
        }

        return result;
    }

    private async Task BroadcastBoard(string shortCode)
    {
        foreach (var (connectionId, userId) in _connections.InRoom(shortCode))
        {
            var snapshot = await _standup.GetByShortCodeAsync(shortCode, userId);
            if (snapshot is not null)
            {
                await Clients.Client(connectionId).SendAsync("BoardUpdated", snapshot);
            }
        }
    }

    private async Task JoinGroupAndTrack(string shortCode, string userId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(shortCode));
        _connections.Track(Context.ConnectionId, shortCode, userId);
    }

    /// <summary>The SignalR group name for a standup room.</summary>
    public static string GroupName(string shortCode) => $"standup:{shortCode}";
}
