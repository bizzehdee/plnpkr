using Microsoft.AspNetCore.SignalR;
using TeamTools.Core;
using TeamTools.Core.Coffee;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;

namespace TeamTools.Api.Hubs;

/// <summary>
/// SignalR hub for real-time Lean Coffee interaction (#35). Thin adapter: every method delegates to
/// <see cref="CoffeeService"/> and broadcasts the result. One group per room, keyed by short code —
/// the same shape as <see cref="PokerHub"/> and <see cref="RetroHub"/>, sharing the room-level
/// connection registry, reaction limiter and abuse throttle rather than reimplementing them (#19).
/// <para>
/// <b>Broadcasts are per recipient</b>, like the retro's: a coffee snapshot depends on who is
/// receiving it — other people's topics during Propose, dot totals while voting, individual
/// extension answers before the reveal — so each connection gets its own projection.
/// </para>
/// </summary>
public class CoffeeHub : Hub
{
    private readonly CoffeeService _coffee;
    private readonly ConnectionRegistry _connections;
    private readonly ReactionRateLimiter _reactions;
    private readonly HubThrottle _throttle;

    public CoffeeHub(
        CoffeeService coffee, ConnectionRegistry connections,
        ReactionRateLimiter reactions, HubThrottle throttle)
    {
        _coffee = coffee;
        _connections = connections;
        _reactions = reactions;
        _throttle = throttle;
    }

    // --- Creation & membership ---------------------------------------------

    public async Task<CreateCoffeeResult> CreateBoard(
        string name, string userId, string displayName, bool organise, string? password,
        bool enableReactions, int? timeboxSeconds)
    {
        if (!_throttle.TryCreate(Context.ConnectionId))
        {
            return CreateCoffeeResult.RateLimited();
        }

        var result = await _coffee.CreateAsync(new CreateCoffeeRequest(
            name, userId, displayName, organise, password, enableReactions, timeboxSeconds));

        if (result.Status == CreateCoffeeStatus.Ok)
        {
            await JoinGroupAndTrack(result.Board!.Room.ShortCode, userId);
        }

        return result;
    }

    public async Task<CoffeeJoinResult> JoinBoard(
        string shortCode, string userId, string displayName, ParticipantRole role, string? password)
    {
        if (!_throttle.TryJoin(Context.ConnectionId))
        {
            return new CoffeeJoinResult(JoinStatus.RateLimited, null, null,
                "You're doing that too often — please wait a moment and try again.");
        }

        var result = await _coffee.JoinAsync(
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
        var result = await _coffee.LeaveAsync(shortCode, userId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(shortCode));
        _connections.TryRemove(Context.ConnectionId, out _);

        if (result.Status == CoffeeActionStatus.Ok)
        {
            await BroadcastBoard(shortCode);
        }
    }

    // --- Topics -------------------------------------------------------------

    public async Task<CoffeeActionResult> AddTopic(string shortCode, string userId, string text)
    {
        // Topics are the high-frequency write here, as cards are on a retro board (#3-abuse).
        if (!_throttle.TryAddCard(Context.ConnectionId))
        {
            return CoffeeActionResult.RateLimited();
        }

        return await MutateAndBroadcast(shortCode, () => _coffee.AddTopicAsync(shortCode, userId, text));
    }

    public Task<CoffeeActionResult> EditTopic(string shortCode, string userId, Guid topicId, string text) =>
        MutateAndBroadcast(shortCode, () => _coffee.EditTopicAsync(shortCode, userId, topicId, text));

    public Task<CoffeeActionResult> DeleteTopic(string shortCode, string userId, Guid topicId) =>
        MutateAndBroadcast(shortCode, () => _coffee.DeleteTopicAsync(shortCode, userId, topicId));

    // --- Dot voting ---------------------------------------------------------

    public Task<CoffeeActionResult> CastVote(string shortCode, string userId, Guid topicId) =>
        MutateAndBroadcast(shortCode, () => _coffee.CastVoteAsync(shortCode, userId, topicId));

    public Task<CoffeeActionResult> WithdrawVote(string shortCode, string userId, Guid topicId) =>
        MutateAndBroadcast(shortCode, () => _coffee.WithdrawVoteAsync(shortCode, userId, topicId));

    public Task<CoffeeActionResult> SetVoteBudget(string shortCode, string userId, int budget) =>
        MutateAndBroadcast(shortCode, () => _coffee.SetVoteBudgetAsync(shortCode, userId, budget));

    public Task<CoffeeActionResult> SetAllowMultiplePerItem(string shortCode, string userId, bool allow) =>
        MutateAndBroadcast(shortCode, () => _coffee.SetAllowMultiplePerItemAsync(shortCode, userId, allow));

    // --- Phases and the discussion -----------------------------------------

    public Task<CoffeeActionResult> AdvancePhase(string shortCode, string userId) =>
        MutateAndBroadcast(shortCode, () => _coffee.AdvancePhaseAsync(shortCode, userId));

    public Task<CoffeeActionResult> PreviousPhase(string shortCode, string userId) =>
        MutateAndBroadcast(shortCode, () => _coffee.PreviousPhaseAsync(shortCode, userId));

    public Task<CoffeeActionResult> SetPhase(string shortCode, string userId, CoffeePhase phase) =>
        MutateAndBroadcast(shortCode, () => _coffee.SetPhaseAsync(shortCode, userId, phase));

    public Task<CoffeeActionResult> SetTimebox(string shortCode, string userId, int? seconds) =>
        MutateAndBroadcast(shortCode, () => _coffee.SetTimeboxAsync(shortCode, userId, seconds));

    public Task<CoffeeActionResult> NextTopic(string shortCode, string userId) =>
        MutateAndBroadcast(shortCode, () => _coffee.NextTopicAsync(shortCode, userId));

    public Task<CoffeeActionResult> VoteOnExtension(string shortCode, string userId, ExtendChoice choice) =>
        MutateAndBroadcast(shortCode, () => _coffee.VoteOnExtensionAsync(shortCode, userId, choice));

    public Task<CoffeeActionResult> ResolveExtension(string shortCode, string userId) =>
        MutateAndBroadcast(shortCode, () => _coffee.ResolveExtensionAsync(shortCode, userId));

    // --- Decisions ----------------------------------------------------------

    public Task<CoffeeActionResult> AddDecision(
        string shortCode, string userId, string title, Guid? topicId, string? ownerUserId,
        string? ownerName, DateTimeOffset? dueDate) =>
        MutateAndBroadcast(shortCode, () => _coffee.AddDecisionAsync(
            shortCode, userId, title, topicId, ownerUserId, ownerName, dueDate));

    public Task<CoffeeActionResult> EditDecision(
        string shortCode, string userId, Guid decisionId, string title, string? ownerUserId,
        string? ownerName, DateTimeOffset? dueDate) =>
        MutateAndBroadcast(shortCode, () => _coffee.EditDecisionAsync(
            shortCode, userId, decisionId, title, ownerUserId, ownerName, dueDate));

    public Task<CoffeeActionResult> ToggleDecisionDone(string shortCode, string userId, Guid decisionId) =>
        MutateAndBroadcast(shortCode, () => _coffee.ToggleDecisionDoneAsync(shortCode, userId, decisionId));

    public Task<CoffeeActionResult> DeleteDecision(string shortCode, string userId, Guid decisionId) =>
        MutateAndBroadcast(shortCode, () => _coffee.DeleteDecisionAsync(shortCode, userId, decisionId));

    // --- Room-level operations ---------------------------------------------

    public Task<CoffeeActionResult> SetReactionsEnabled(string shortCode, string userId, bool enabled) =>
        MutateAndBroadcast(shortCode, () => _coffee.SetReactionsEnabledAsync(shortCode, userId, enabled));

    public Task<CoffeeActionResult> SetAllowRoleChange(string shortCode, string userId, bool enabled) =>
        MutateAndBroadcast(shortCode, () => _coffee.SetAllowRoleChangeAsync(shortCode, userId, enabled));

    public Task<CoffeeActionResult> ChangeRole(
        string shortCode, string userId, string targetUserId, ParticipantRole role) =>
        MutateAndBroadcast(shortCode, () => _coffee.ChangeRoleAsync(shortCode, userId, targetUserId, role));

    public Task<CoffeeActionResult> PromoteToOrganiser(string shortCode, string userId, string targetUserId) =>
        MutateAndBroadcast(shortCode, () => _coffee.PromoteToOrganiserAsync(shortCode, userId, targetUserId));

    public Task<CoffeeActionResult> DemoteOrganiser(string shortCode, string userId, string targetUserId) =>
        MutateAndBroadcast(shortCode, () => _coffee.DemoteOrganiserAsync(shortCode, userId, targetUserId));

    public Task<CoffeeActionResult> TransferOrganiser(string shortCode, string userId, string targetUserId) =>
        MutateAndBroadcast(shortCode, () => _coffee.TransferOrganiserAsync(shortCode, userId, targetUserId));

    public Task<CoffeeActionResult> SetPassword(string shortCode, string userId, string? password) =>
        MutateAndBroadcast(shortCode, () => _coffee.SetPasswordAsync(shortCode, userId, password));

    public Task<CoffeeActionResult> CloseBoard(string shortCode, string userId) =>
        MutateAndBroadcast(shortCode, () => _coffee.CloseBoardAsync(shortCode, userId));

    public async Task<CoffeeActionResult> DeleteBoard(string shortCode, string userId)
    {
        var result = await _coffee.DeleteBoardAsync(shortCode, userId);
        if (result.Status == CoffeeActionStatus.Ok)
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

        if (!await _coffee.AreReactionsEnabledAsync(info.ShortCode))
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
            var result = await _coffee.MarkDisconnectedAsync(info.ShortCode, info.UserId);
            if (result.Status == CoffeeActionStatus.Ok)
            {
                await BroadcastBoard(info.ShortCode);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // --- Plumbing -----------------------------------------------------------

    private async Task<CoffeeActionResult> MutateAndBroadcast(
        string shortCode, Func<Task<CoffeeActionResult>> mutate)
    {
        var result = await mutate();
        if (result.Status == CoffeeActionStatus.Ok)
        {
            await BroadcastBoard(shortCode);
        }

        return result;
    }

    /// <summary>
    /// Sends every connection in the room its own projection. Per connection, not per group,
    /// because what a recipient may see differs — see the class remarks.
    /// </summary>
    private async Task BroadcastBoard(string shortCode)
    {
        foreach (var (connectionId, userId) in _connections.InRoom(shortCode))
        {
            var snapshot = await _coffee.GetByShortCodeAsync(shortCode, userId);
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

    /// <summary>The SignalR group name for a coffee room.</summary>
    public static string GroupName(string shortCode) => $"coffee:{shortCode}";
}
