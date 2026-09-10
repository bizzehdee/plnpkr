using Microsoft.AspNetCore.SignalR;
using TeamTools.Core;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;

namespace TeamTools.Api.Hubs;

/// <summary>
/// SignalR hub for real-time retro-board interaction (#21). Thin adapter: every method delegates to
/// <see cref="RetroService"/> and broadcasts the result to the room's group. One group per room,
/// keyed by short code — the same shape as <see cref="PokerHub"/>, and it shares the room-level
/// connection registry, reaction limiter and abuse throttle rather than reimplementing them (#19).
/// <para>
/// <b>Broadcasts are per recipient.</b> A retro snapshot depends on who is receiving it (authorship
/// under anonymity #22, hidden collection #23), so the hub sends each connection its own projection
/// instead of one payload to the group. That is the cost of making those properties of the wire.
/// </para>
/// </summary>
public class RetroHub : Hub
{
    private readonly RetroService _retro;
    private readonly ConnectionRegistry _connections;
    private readonly ReactionRateLimiter _reactions;
    private readonly HubThrottle _throttle;

    public RetroHub(
        RetroService retro, ConnectionRegistry connections,
        ReactionRateLimiter reactions, HubThrottle throttle)
    {
        _retro = retro;
        _connections = connections;
        _reactions = reactions;
        _throttle = throttle;
    }

    // --- Creation & membership ---------------------------------------------

    public async Task<CreateRetroResult> CreateBoard(
        string name, RetroTemplate template, string? customColumns, string userId, string displayName,
        bool organise, string? password, bool enableReactions, bool anonymous)
    {
        // Throttle anonymous board creation per connection (#3-abuse).
        if (!_throttle.TryCreate(Context.ConnectionId))
        {
            return CreateRetroResult.RateLimited();
        }

        var result = await _retro.CreateAsync(new CreateRetroRequest(
            name, template, customColumns, userId, displayName, organise, password, enableReactions,
            anonymous));

        if (result.Status == CreateRetroStatus.Ok)
        {
            await JoinGroupAndTrack(result.Board!.Room.ShortCode, userId);
        }

        return result;
    }

    public async Task<RetroJoinResult> JoinBoard(
        string shortCode, string userId, string displayName, ParticipantRole role, string? password)
    {
        // Throttle join attempts per connection — blunts password-guessing and join floods (#3-abuse).
        if (!_throttle.TryJoin(Context.ConnectionId))
        {
            return new RetroJoinResult(JoinStatus.RateLimited, null, null,
                "You're doing that too often — please wait a moment and try again.");
        }

        var result = await _retro.JoinAsync(new JoinSessionRequest(shortCode, userId, displayName, role, password));

        if (result.Status == JoinStatus.Ok)
        {
            await JoinGroupAndTrack(shortCode, userId);
            await BroadcastBoard(shortCode);
        }

        return result;
    }

    public async Task LeaveBoard(string shortCode, string userId)
    {
        var result = await _retro.LeaveAsync(shortCode, userId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(shortCode));
        _connections.TryRemove(Context.ConnectionId, out _);

        if (result.Status == RetroActionStatus.Ok)
        {
            await BroadcastBoard(shortCode);
        }
    }

    // --- Cards --------------------------------------------------------------

    public async Task<RetroActionResult> AddCard(string shortCode, string userId, Guid columnId, string text)
    {
        // Cards are the one high-frequency write on a board, so they get their own window (#3-abuse).
        if (!_throttle.TryAddCard(Context.ConnectionId))
        {
            return RetroActionResult.RateLimited();
        }

        return await MutateAndBroadcast(shortCode, () => _retro.AddCardAsync(shortCode, userId, columnId, text));
    }

    public Task<RetroActionResult> EditCard(string shortCode, string userId, Guid cardId, string text) =>
        MutateAndBroadcast(shortCode, () => _retro.EditCardAsync(shortCode, userId, cardId, text));

    public Task<RetroActionResult> DeleteCard(string shortCode, string userId, Guid cardId) =>
        MutateAndBroadcast(shortCode, () => _retro.DeleteCardAsync(shortCode, userId, cardId));

    public Task<RetroActionResult> MoveCard(
        string shortCode, string userId, Guid cardId, Guid targetColumnId, int targetOrder) =>
        MutateAndBroadcast(shortCode, () =>
            _retro.MoveCardAsync(shortCode, userId, cardId, targetColumnId, targetOrder));

    public Task<RetroActionResult> SetTemplate(
        string shortCode, string userId, RetroTemplate template, string? customColumns) =>
        MutateAndBroadcast(shortCode, () =>
            _retro.SetTemplateAsync(shortCode, userId, template, customColumns));

    public Task<RetroActionResult> SetAnonymous(string shortCode, string userId, bool anonymous) =>
        MutateAndBroadcast(shortCode, () => _retro.SetAnonymousAsync(shortCode, userId, anonymous));

    // --- Phases (#23), organiser-gated --------------------------------------

    public Task<RetroActionResult> AdvancePhase(string shortCode, string userId, int? seconds) =>
        MutateAndBroadcast(shortCode, () => _retro.AdvancePhaseAsync(shortCode, userId, seconds));

    public Task<RetroActionResult> PreviousPhase(string shortCode, string userId) =>
        MutateAndBroadcast(shortCode, () => _retro.PreviousPhaseAsync(shortCode, userId));

    public Task<RetroActionResult> SetPhase(
        string shortCode, string userId, RetroPhase phase, int? seconds) =>
        MutateAndBroadcast(shortCode, () => _retro.SetPhaseAsync(shortCode, userId, phase, seconds));

    public Task<RetroActionResult> SetPhaseDuration(string shortCode, string userId, int? seconds) =>
        MutateAndBroadcast(shortCode, () => _retro.SetPhaseDurationAsync(shortCode, userId, seconds));

    // --- Grouping (#24) -----------------------------------------------------

    public Task<RetroActionResult> GroupCards(
        string shortCode, string userId, Guid[] cardIds, Guid? targetGroupId) =>
        MutateAndBroadcast(shortCode, () =>
            _retro.GroupCardsAsync(shortCode, userId, cardIds, targetGroupId));

    public Task<RetroActionResult> UngroupCard(string shortCode, string userId, Guid cardId) =>
        MutateAndBroadcast(shortCode, () => _retro.UngroupCardAsync(shortCode, userId, cardId));

    public Task<RetroActionResult> RenameGroup(
        string shortCode, string userId, Guid groupId, string label) =>
        MutateAndBroadcast(shortCode, () => _retro.RenameGroupAsync(shortCode, userId, groupId, label));

    public Task<RetroActionResult> SetAllowParticipantGrouping(
        string shortCode, string userId, bool allowed) =>
        MutateAndBroadcast(shortCode, () =>
            _retro.SetAllowParticipantGroupingAsync(shortCode, userId, allowed));

    // --- Dot voting (#25) ---------------------------------------------------

    public Task<RetroActionResult> CastRetroVote(
        string shortCode, string userId, RetroVoteTarget kind, Guid targetId) =>
        MutateAndBroadcast(shortCode, () => _retro.CastVoteAsync(shortCode, userId, kind, targetId));

    public Task<RetroActionResult> WithdrawVote(
        string shortCode, string userId, RetroVoteTarget kind, Guid targetId) =>
        MutateAndBroadcast(shortCode, () => _retro.WithdrawVoteAsync(shortCode, userId, kind, targetId));

    public Task<RetroActionResult> SetVoteBudget(
        string shortCode, string userId, int budget, bool allowMultiplePerItem) =>
        MutateAndBroadcast(shortCode, () =>
            _retro.SetVoteBudgetAsync(shortCode, userId, budget, allowMultiplePerItem));

    // --- Room-level settings & lifecycle ------------------------------------

    public Task<RetroActionResult> SetReactionsEnabled(string shortCode, string userId, bool enabled) =>
        MutateAndBroadcast(shortCode, () => _retro.SetReactionsEnabledAsync(shortCode, userId, enabled));

    public Task<RetroActionResult> SetAllowRoleChange(string shortCode, string userId, bool enabled) =>
        MutateAndBroadcast(shortCode, () => _retro.SetAllowRoleChangeAsync(shortCode, userId, enabled));

    public Task<RetroActionResult> ChangeRole(
        string shortCode, string userId, string targetUserId, ParticipantRole role) =>
        MutateAndBroadcast(shortCode, () => _retro.ChangeRoleAsync(shortCode, userId, targetUserId, role));

    public Task<RetroActionResult> PromoteToOrganiser(string shortCode, string userId, string targetUserId) =>
        MutateAndBroadcast(shortCode, () => _retro.PromoteToOrganiserAsync(shortCode, userId, targetUserId));

    public Task<RetroActionResult> DemoteOrganiser(string shortCode, string userId, string targetUserId) =>
        MutateAndBroadcast(shortCode, () => _retro.DemoteOrganiserAsync(shortCode, userId, targetUserId));

    public Task<RetroActionResult> TransferOrganiser(string shortCode, string userId, string targetUserId) =>
        MutateAndBroadcast(shortCode, () => _retro.TransferOrganiserAsync(shortCode, userId, targetUserId));

    public Task<RetroActionResult> SetPassword(string shortCode, string userId, string? password) =>
        MutateAndBroadcast(shortCode, () => _retro.SetPasswordAsync(shortCode, userId, password));

    public Task<RetroActionResult> CloseBoard(string shortCode, string userId) =>
        MutateAndBroadcast(shortCode, () => _retro.CloseBoardAsync(shortCode, userId));

    public async Task<RetroActionResult> DeleteBoard(string shortCode, string userId)
    {
        var result = await _retro.DeleteBoardAsync(shortCode, userId);
        if (result.Status == RetroActionStatus.Ok)
        {
            // The board is gone from every read, so there is no snapshot to send — tell the room.
            await Clients.Group(GroupName(shortCode)).SendAsync("BoardClosed");
        }

        return result;
    }

    // Ephemeral emoji reaction (#17), shared with poker: not persisted, fan-out only. The room/user
    // come from the tracked connection (can't be spoofed); allowlist + per-connection rate limit
    // guard against spam. Silently ignored if disallowed/over-limit/not in a room.
    public async Task React(string emoji)
    {
        if (!ReactionPolicy.IsAllowed(emoji)
            || !_connections.TryGet(Context.ConnectionId, out var info)
            || !_reactions.TryReact(Context.ConnectionId))
        {
            return;
        }

        if (!await _retro.AreReactionsEnabledAsync(info.ShortCode))
        {
            return;
        }

        await Clients.Group(GroupName(info.ShortCode)).SendAsync("ReactionReceived", info.UserId, emoji);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _reactions.Forget(Context.ConnectionId);
        _throttle.Forget(Context.ConnectionId);

        // A drop marks the participant away rather than removing them; idle eviction cleans up if
        // they don't return. See #34.
        if (_connections.TryRemove(Context.ConnectionId, out var info))
        {
            var result = await _retro.MarkDisconnectedAsync(info.ShortCode, info.UserId);
            if (result.Status == RetroActionStatus.Ok)
            {
                await BroadcastBoard(info.ShortCode);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // --- Plumbing -----------------------------------------------------------

    private async Task<RetroActionResult> MutateAndBroadcast(
        string shortCode, Func<Task<RetroActionResult>> action)
    {
        var result = await action();
        if (result.Status == RetroActionStatus.Ok)
        {
            await BroadcastBoard(shortCode);
        }

        return result;
    }

    /// <summary>
    /// Sends every connection in the room its own projection of the board. Per connection, not per
    /// group, because what a recipient may see differs — see the class remarks.
    /// </summary>
    private async Task BroadcastBoard(string shortCode)
    {
        foreach (var (connectionId, userId) in _connections.InRoom(shortCode))
        {
            var snapshot = await _retro.GetByShortCodeAsync(shortCode, userId);
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

    /// <summary>The SignalR group name for a retro room. Shared with the eviction service.</summary>
    public static string GroupName(string shortCode) => $"retro:{shortCode}";
}
