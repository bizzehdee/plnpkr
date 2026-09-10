using TeamTools.Core.Contracts;
using TeamTools.Core.Models;

namespace TeamTools.Core.Coffee;

/// <summary>
/// Lean Coffee decision-making (#35): propose topics, rank them with dots, then work the ranked
/// list one topic at a time under a timebox, recording what the room decided.
/// <para>
/// Room concerns (join, leave, presence, roles, organisers, password, close/delete) are delegated to
/// <see cref="RoomService"/>, exactly as the other two tools do. The dot budget is the shared
/// <see cref="DotBudget"/> primitive, the phase rail is the shared <see cref="PhaseRail{TPhase}"/>,
/// the countdown is <see cref="Countdown"/>, and decisions follow <see cref="ActionItemRules"/> —
/// so what is actually new here is the per-topic timebox and the extension vote.
/// </para>
/// </summary>
public class CoffeeService
{
    /// <summary>Longest a topic may be. A sentence, not an agenda.</summary>
    public const int MaxTopicLength = 300;

    /// <summary>Bounds for the dot budget, matching the retro's.</summary>
    public const int MinVoteBudget = 1;
    public const int MaxVoteBudget = 20;

    private readonly IRoomStore _store;
    private readonly RoomService _rooms;
    private readonly IClock _clock;

    public CoffeeService(IRoomStore store, RoomService rooms, IClock clock)
    {
        _store = store;
        _rooms = rooms;
        _clock = clock;
    }

    // --- Creation ----------------------------------------------------------

    public async Task<CreateCoffeeResult> CreateAsync(
        CreateCoffeeRequest request, CancellationToken ct = default)
    {
        if (NameNormalizer.IsBlank(request.Name))
        {
            return CreateCoffeeResult.InvalidName("A name is required.");
        }

        if (NameNormalizer.IsBlank(request.CreatorDisplayName))
        {
            return CreateCoffeeResult.InvalidName("Your display name is required.");
        }

        var room = await _rooms.NewRoomAsync(
            RoomTool.Coffee, request.Name, request.CreatorUserId, request.CreatorDisplayName,
            request.Organise, request.EnableReactions, request.Password, ct);

        room.CoffeeBoard = new CoffeeBoard
        {
            RoomId = room.Id,
            PhaseDurationSeconds =
                CoffeePhaseRules.NormalizeTimebox(request.TimeboxSeconds)
                ?? CoffeePhaseRules.DefaultTimeboxSeconds,
        };

        await _rooms.AddAsync(room, ct);
        return CreateCoffeeResult.Ok(ToSnapshot(room, request.CreatorUserId));
    }

    // --- Room-level operations, projected as coffee results ----------------

    public async Task<CoffeeJoinResult> JoinAsync(
        JoinSessionRequest request, CancellationToken ct = default)
    {
        var outcome = await _rooms.JoinAsync(request, RoomTool.Coffee, ct);
        if (outcome.Status != JoinStatus.Ok)
        {
            return new CoffeeJoinResult(outcome.Status, null, null, outcome.Error);
        }

        var room = outcome.Room!;
        return CoffeeJoinResult.Ok(
            ToSnapshot(room, request.UserId),
            RoomProjection.ToInfo(outcome.Participant!, revealed: false));
    }

    public async Task<CoffeeActionResult> LeaveAsync(
        string shortCode, string userId, CancellationToken ct = default)
    {
        var (status, room) = await _rooms.LeaveAsync(shortCode, userId, ct);
        return status == LeaveStatus.Ok
            ? CoffeeActionResult.Ok(ToSnapshot(room!, userId))
            : CoffeeActionResult.NotFound();
    }

    public async Task<CoffeeActionResult> MarkDisconnectedAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.MarkDisconnectedAsync(shortCode, userId, ct), userId);

    public async Task<CoffeeActionResult> ChangeRoleAsync(
        string shortCode, string actingUserId, string targetUserId, ParticipantRole role,
        CancellationToken ct = default) =>
        Project(await _rooms.ChangeRoleAsync(shortCode, actingUserId, targetUserId, role, ct: ct), actingUserId);

    public async Task<CoffeeActionResult> SetAllowRoleChangeAsync(
        string shortCode, string userId, bool enabled, CancellationToken ct = default) =>
        Project(await _rooms.SetAllowRoleChangeAsync(shortCode, userId, enabled, ct), userId);

    public async Task<CoffeeActionResult> SetReactionsEnabledAsync(
        string shortCode, string userId, bool enabled, CancellationToken ct = default) =>
        Project(await _rooms.SetReactionsEnabledAsync(shortCode, userId, enabled, ct), userId);

    public Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken ct = default) =>
        _rooms.AreReactionsEnabledAsync(shortCode, ct);

    public async Task<CoffeeActionResult> PromoteToOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.PromoteToOrganiserAsync(shortCode, actingUserId, targetUserId, ct), actingUserId);

    public async Task<CoffeeActionResult> DemoteOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.DemoteOrganiserAsync(shortCode, actingUserId, targetUserId, ct), actingUserId);

    public async Task<CoffeeActionResult> TransferOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.TransferOrganiserAsync(shortCode, actingUserId, targetUserId, ct), actingUserId);

    public async Task<CoffeeActionResult> SetPasswordAsync(
        string shortCode, string userId, string? password, CancellationToken ct = default) =>
        Project(await _rooms.SetPasswordAsync(shortCode, userId, password, ct), userId);

    public async Task<CoffeeActionResult> CloseBoardAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.CloseRoomAsync(shortCode, userId, ct: ct), userId);

    public async Task<CoffeeActionResult> DeleteBoardAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.DeleteRoomAsync(shortCode, userId, ct), userId);

    // --- Topics ------------------------------------------------------------

    /// <summary>Proposes a topic. Any participant, during Propose only.</summary>
    public async Task<CoffeeActionResult> AddTopicAsync(
        string shortCode, string userId, string text, CancellationToken ct = default)
    {
        var (room, error) = await LoadForParticipantAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (!CoffeePhaseRules.TopicsWritable(board.Phase))
        {
            // Past Propose, a new topic would make the ranking a lie.
            return CoffeeActionResult.WrongPhase();
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxTopicLength)
        {
            return CoffeeActionResult.InvalidTopicText();
        }

        board.Topics.Add(new CoffeeTopic
        {
            Id = Guid.NewGuid(),
            BoardId = board.RoomId,
            AuthorUserId = userId,
            Text = trimmed,
            Order = board.Topics.Count,
            CreatedAt = _clock.UtcNow,
        });

        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>Rewords a topic. Its author, or an organiser moderating.</summary>
    public async Task<CoffeeActionResult> EditTopicAsync(
        string shortCode, string userId, Guid topicId, string text, CancellationToken ct = default)
    {
        var (room, topic, error) = await LoadTopicForWriteAsync(shortCode, userId, topicId, ct);
        if (error is not null)
        {
            return error;
        }

        if (!CoffeePhaseRules.TopicsWritable(Board(room!).Phase))
        {
            return CoffeeActionResult.WrongPhase();
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxTopicLength)
        {
            return CoffeeActionResult.InvalidTopicText();
        }

        topic!.Text = trimmed;
        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>Withdraws a topic, and any dots spent on it.</summary>
    public async Task<CoffeeActionResult> DeleteTopicAsync(
        string shortCode, string userId, Guid topicId, CancellationToken ct = default)
    {
        var (room, topic, error) = await LoadTopicForWriteAsync(shortCode, userId, topicId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (!CoffeePhaseRules.TopicsWritable(board.Phase))
        {
            return CoffeeActionResult.WrongPhase();
        }

        board.Topics.Remove(topic!);
        // Dots on a withdrawn topic are gone with it — leaving them would let a voter's budget be
        // silently consumed by something nobody can vote for.
        board.Votes.RemoveAll(v => v.TargetId == topicId);
        Reorder(board);

        return await CommitAsync(room!, userId, ct);
    }

    // --- Dot voting --------------------------------------------------------

    /// <summary>Spends one dot on a topic. The budget is enforced from the stored rows.</summary>
    public async Task<CoffeeActionResult> CastVoteAsync(
        string shortCode, string userId, Guid topicId, CancellationToken ct = default)
    {
        var (room, error) = await LoadForParticipantAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (!CoffeePhaseRules.VotingAllowed(board.Phase))
        {
            return CoffeeActionResult.WrongPhase();
        }

        if (board.Topics.All(t => t.Id != topicId))
        {
            return CoffeeActionResult.TopicNotFound();
        }

        // The shared budget rule (#35): counted from rows, never from anything the client sent.
        var spend = DotBudget.CanSpend(
            board.Votes, board.VoteBudget, userId, topicId, board.AllowMultiplePerItem);
        if (spend == DotSpendStatus.OutOfDots)
        {
            return CoffeeActionResult.OutOfDots();
        }
        if (spend == DotSpendStatus.AlreadyVotedForItem)
        {
            return CoffeeActionResult.AlreadyVotedForItem();
        }

        board.Votes.Add(new CoffeeVote
        {
            Id = Guid.NewGuid(),
            BoardId = board.RoomId,
            VoterUserId = userId,
            TargetId = topicId,
        });

        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>Takes one dot back off a topic.</summary>
    public async Task<CoffeeActionResult> WithdrawVoteAsync(
        string shortCode, string userId, Guid topicId, CancellationToken ct = default)
    {
        var (room, error) = await LoadForParticipantAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (!CoffeePhaseRules.VotingAllowed(board.Phase))
        {
            return CoffeeActionResult.WrongPhase();
        }

        var mine = board.Votes.FirstOrDefault(v => v.VoterUserId == userId && v.TargetId == topicId);
        if (mine is null)
        {
            return CoffeeActionResult.NoVoteToWithdraw();
        }

        board.Votes.Remove(mine);
        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>Organiser-only: change how many dots each participant gets.</summary>
    public async Task<CoffeeActionResult> SetVoteBudgetAsync(
        string shortCode, string userId, int budget, CancellationToken ct = default)
    {
        if (budget < MinVoteBudget || budget > MaxVoteBudget)
        {
            return CoffeeActionResult.InvalidVoteBudget();
        }

        return await ControlAsync(shortCode, userId, board => board.VoteBudget = budget, ct);
    }

    /// <summary>Organiser-only: allow or forbid stacking dots on one topic.</summary>
    public Task<CoffeeActionResult> SetAllowMultiplePerItemAsync(
        string shortCode, string userId, bool allow, CancellationToken ct = default) =>
        ControlAsync(shortCode, userId, board => board.AllowMultiplePerItem = allow, ct);

    // --- Phases ------------------------------------------------------------

    public Task<CoffeeActionResult> AdvancePhaseAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        MovePhaseAsync(shortCode, userId, forward: true, ct);

    public Task<CoffeeActionResult> PreviousPhaseAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        MovePhaseAsync(shortCode, userId, forward: false, ct);

    /// <summary>Organiser-only: move to a named adjacent phase.</summary>
    public async Task<CoffeeActionResult> SetPhaseAsync(
        string shortCode, string userId, CoffeePhase phase, CancellationToken ct = default)
    {
        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (!CoffeePhaseRules.IsLegalTransition(board.Phase, phase))
        {
            return CoffeeActionResult.IllegalPhaseTransition();
        }

        ApplyPhase(board, phase);
        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>Organiser-only: set or clear the per-topic timebox length.</summary>
    public Task<CoffeeActionResult> SetTimeboxAsync(
        string shortCode, string userId, int? seconds, CancellationToken ct = default) =>
        ControlAsync(
            shortCode, userId,
            board => board.PhaseDurationSeconds = CoffeePhaseRules.NormalizeTimebox(seconds), ct);

    private async Task<CoffeeActionResult> MovePhaseAsync(
        string shortCode, string userId, bool forward, CancellationToken ct)
    {
        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        var target = forward
            ? CoffeePhaseRules.Next(board.Phase)
            : CoffeePhaseRules.Previous(board.Phase);
        if (target is null)
        {
            return CoffeeActionResult.IllegalPhaseTransition();
        }

        ApplyPhase(board, target.Value);
        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>
    /// Moves the board to a phase and does whatever that phase implies.
    /// <para>
    /// Entering Discuss starts the room on the highest-voted topic and its timebox; leaving it
    /// stops the clock. Nothing else auto-advances — see <see cref="CoffeeTimerService"/> for why a
    /// timebox running out does not move the room on by itself.
    /// </para>
    /// </summary>
    private void ApplyPhase(CoffeeBoard board, CoffeePhase phase)
    {
        board.Phase = phase;
        board.ExtendVoteOpen = false;
        board.ExtendVotes.Clear();

        if (phase == CoffeePhase.Discuss)
        {
            StartNextTopic(board);
        }
        else
        {
            board.CurrentTopicId = null;
            board.PhaseDeadline = null;
        }
    }

    // --- The discussion ----------------------------------------------------

    /// <summary>
    /// Organiser-only: finish with the current topic and start the next one. What the facilitator
    /// presses when the room is done talking, whether or not the timebox has run out.
    /// </summary>
    public async Task<CoffeeActionResult> NextTopicAsync(
        string shortCode, string userId, CancellationToken ct = default)
    {
        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (!CoffeePhaseRules.DiscussionRunning(board.Phase))
        {
            return CoffeeActionResult.WrongPhase();
        }

        FinishCurrentTopic(board);
        StartNextTopic(board);
        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>
    /// Any participant: answer the running extension vote. Answers are hidden until the facilitator
    /// resolves it, so the room does not simply follow whoever clicked first.
    /// </summary>
    public async Task<CoffeeActionResult> VoteOnExtensionAsync(
        string shortCode, string userId, ExtendChoice choice, CancellationToken ct = default)
    {
        var (room, error) = await LoadForParticipantAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (!board.ExtendVoteOpen || board.CurrentTopicId is not { } topicId)
        {
            return CoffeeActionResult.NoExtendVoteRunning();
        }

        var existing = board.ExtendVotes.FirstOrDefault(v => v.VoterUserId == userId);
        if (existing is not null)
        {
            existing.Choice = choice; // changing your mind before the reveal is fine
        }
        else
        {
            board.ExtendVotes.Add(new CoffeeExtendVote
            {
                Id = Guid.NewGuid(),
                BoardId = board.RoomId,
                TopicId = topicId,
                VoterUserId = userId,
                Choice = choice,
            });
        }

        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>
    /// Organiser-only: close the extension vote and act on it. A majority to keep going restarts
    /// the timebox on the same topic and counts an extension; otherwise the room moves on.
    /// <para>
    /// The facilitator resolves it rather than the server auto-deciding, because "the vote was 3-2,
    /// let's give it two more minutes" is a judgement a person makes with the room in front of them.
    /// </para>
    /// </summary>
    public async Task<CoffeeActionResult> ResolveExtensionAsync(
        string shortCode, string userId, CancellationToken ct = default)
    {
        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (!board.ExtendVoteOpen || board.CurrentTopicId is not { } topicId)
        {
            return CoffeeActionResult.NoExtendVoteRunning();
        }

        var keepGoing = board.ExtendVotes.Count(v => v.Choice == ExtendChoice.KeepGoing);
        var moveOn = board.ExtendVotes.Count(v => v.Choice == ExtendChoice.MoveOn);
        board.ExtendVoteOpen = false;

        if (keepGoing > moveOn)
        {
            var topic = board.Topics.FirstOrDefault(t => t.Id == topicId);
            if (topic is not null)
            {
                topic.Extensions++;
            }
            board.PhaseDeadline = Countdown.DeadlineFrom(board.PhaseDurationSeconds, _clock.UtcNow);
            board.ExtendVotes.Clear();
        }
        else
        {
            FinishCurrentTopic(board);
            StartNextTopic(board);
        }

        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>
    /// Starts the room on the highest-voted topic it has not discussed yet, and its timebox. Clears
    /// the current topic when there is nothing left — the signal for "you have worked the list".
    /// </summary>
    internal void StartNextTopic(CoffeeBoard board)
    {
        var next = Agenda(board).FirstOrDefault(t => !t.IsDiscussed);
        board.CurrentTopicId = next?.Id;
        board.ExtendVoteOpen = false;
        board.ExtendVotes.Clear();

        if (next is null)
        {
            board.PhaseDeadline = null;
            return;
        }

        next.StartedAt ??= _clock.UtcNow;
        board.PhaseDeadline = Countdown.DeadlineFrom(board.PhaseDurationSeconds, _clock.UtcNow);
    }

    /// <summary>
    /// Closes the current topic, accumulating the time actually spent on it. Called on "next topic",
    /// on a move-on vote, and when the phase leaves Discuss.
    /// </summary>
    internal void FinishCurrentTopic(CoffeeBoard board)
    {
        if (board.CurrentTopicId is not { } id)
        {
            return;
        }

        var topic = board.Topics.FirstOrDefault(t => t.Id == id);
        if (topic is null)
        {
            return;
        }

        topic.DiscussedSeconds += SecondsSpentOnCurrent(board);
        topic.FinishedAt = _clock.UtcNow;
    }

    /// <summary>
    /// Adds the stretch that just ran to the current topic's total, without closing the topic —
    /// what timebox expiry needs before it opens the extension vote (#35).
    /// </summary>
    internal void AccumulateElapsed(CoffeeBoard board)
    {
        if (board.CurrentTopicId is not { } id)
        {
            return;
        }

        var topic = board.Topics.FirstOrDefault(t => t.Id == id);
        if (topic is not null)
        {
            topic.DiscussedSeconds += SecondsSpentOnCurrent(board);
        }
    }

    /// <summary>
    /// How long the current stretch has run. Derived from the deadline rather than tracked
    /// separately, so it stays correct across a server restart: the deadline is the persisted fact.
    /// </summary>
    private int SecondsSpentOnCurrent(CoffeeBoard board)
    {
        if (board.PhaseDurationSeconds is not { } duration || board.PhaseDeadline is not { } deadline)
        {
            return 0;
        }

        var remaining = (deadline - _clock.UtcNow).TotalSeconds;
        return (int)Math.Round(Math.Clamp(duration - remaining, 0, duration));
    }

    // --- Decisions ---------------------------------------------------------

    /// <summary>Records something the room decided, optionally against the current topic.</summary>
    public async Task<CoffeeActionResult> AddDecisionAsync(
        string shortCode, string userId, string title, Guid? topicId, string? ownerUserId,
        string? ownerName, DateTimeOffset? dueDate, CancellationToken ct = default)
    {
        var (room, error) = await LoadForDecisionWriteAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (!CoffeePhaseRules.DecisionsWritable(board.Phase) && !room!.IsClosed)
        {
            return CoffeeActionResult.WrongPhase();
        }

        if (ActionItemRules.NormalizeTitle(title) is not { } trimmed)
        {
            return CoffeeActionResult.InvalidDecisionTitle();
        }

        if (topicId is { } id && board.Topics.All(t => t.Id != id))
        {
            return CoffeeActionResult.TopicNotFound();
        }

        board.Decisions.Add(new CoffeeDecision
        {
            Id = Guid.NewGuid(),
            BoardId = board.RoomId,
            Title = trimmed,
            TopicId = topicId,
            OwnerUserId = ownerUserId,
            OwnerName = ActionItemRules.ResolveOwnerName(room!, ownerUserId, ownerName),
            DueDate = dueDate,
            CreatedAt = _clock.UtcNow,
        });

        return await CommitAsync(room!, userId, ct);
    }

    public async Task<CoffeeActionResult> EditDecisionAsync(
        string shortCode, string userId, Guid decisionId, string title, string? ownerUserId,
        string? ownerName, DateTimeOffset? dueDate, CancellationToken ct = default)
    {
        var (room, decision, error) = await LoadDecisionAsync(shortCode, userId, decisionId, ct);
        if (error is not null)
        {
            return error;
        }

        if (ActionItemRules.NormalizeTitle(title) is not { } trimmed)
        {
            return CoffeeActionResult.InvalidDecisionTitle();
        }

        decision!.Title = trimmed;
        decision.OwnerUserId = ownerUserId;
        decision.OwnerName = ActionItemRules.ResolveOwnerName(room!, ownerUserId, ownerName);
        decision.DueDate = dueDate;

        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>
    /// Marks a decision done, or reopens it. Works on a closed room, for the same reason the
    /// retro's action items do (#26): "we did that" gets ticked days later.
    /// </summary>
    public async Task<CoffeeActionResult> ToggleDecisionDoneAsync(
        string shortCode, string userId, Guid decisionId, CancellationToken ct = default)
    {
        var (room, decision, error) = await LoadDecisionAsync(shortCode, userId, decisionId, ct);
        if (error is not null)
        {
            return error;
        }

        decision!.DoneAt = decision.DoneAt is null ? _clock.UtcNow : null;
        return await CommitAsync(room!, userId, ct);
    }

    public async Task<CoffeeActionResult> DeleteDecisionAsync(
        string shortCode, string userId, Guid decisionId, CancellationToken ct = default)
    {
        var (room, decision, error) = await LoadDecisionAsync(shortCode, userId, decisionId, ct);
        if (error is not null)
        {
            return error;
        }

        Board(room!).Decisions.Remove(decision!);
        return await CommitAsync(room!, userId, ct);
    }

    // --- Reads -------------------------------------------------------------

    /// <summary>The board as <paramref name="forUserId"/> may see it.</summary>
    public async Task<CoffeeBoardSnapshot?> GetByShortCodeAsync(
        string shortCode, string forUserId, CancellationToken ct = default)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        return room?.CoffeeBoard is null ? null : ToSnapshot(room, forUserId);
    }

    // --- Helpers -----------------------------------------------------------

    private static CoffeeBoard Board(Room room) =>
        room.CoffeeBoard ?? throw new InvalidOperationException(
            $"Room '{room.ShortCode}' hosts {room.Tool}, not Coffee — it has no coffee board.");

    /// <summary>Topics ranked by dots, highest first, ties on proposal order. The shared rule.</summary>
    private static IReadOnlyList<CoffeeTopic> Agenda(CoffeeBoard board) =>
        DotBudget.Rank(
            board.Topics,
            t => DotBudget.TotalOn(board.Votes, t.Id),
            t => t.Order);

    private async Task<(Room? Room, CoffeeActionResult? Error)> LoadForParticipantAsync(
        string shortCode, string userId, CancellationToken ct)
    {
        var (room, error) = await _rooms.LoadForParticipantAsync(shortCode, userId, ct);
        return error is not null ? (null, Project(error, userId)) : (room, null);
    }

    private async Task<(Room? Room, CoffeeActionResult? Error)> LoadForControlAsync(
        string shortCode, string userId, CancellationToken ct)
    {
        var (room, error) = await _rooms.LoadForControlAsync(shortCode, userId, ct);
        return error is not null ? (null, Project(error, userId)) : (room, null);
    }

    /// <summary>
    /// Loads a room for a decision write. The one carve-out in the closed-room rule, matching the
    /// retro's (#26): everything else on a closed room is frozen, but decisions stay editable.
    /// </summary>
    private async Task<(Room? Room, CoffeeActionResult? Error)> LoadForDecisionWriteAsync(
        string shortCode, string userId, CancellationToken ct)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        if (room?.CoffeeBoard is null)
        {
            return (null, CoffeeActionResult.NotFound());
        }

        if (room.Participants.All(p => p.UserId != userId))
        {
            return (null, CoffeeActionResult.NotParticipant());
        }

        return (room, null);
    }

    private async Task<(Room? Room, CoffeeDecision? Decision, CoffeeActionResult? Error)>
        LoadDecisionAsync(string shortCode, string userId, Guid decisionId, CancellationToken ct)
    {
        var (room, error) = await LoadForDecisionWriteAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return (null, null, error);
        }

        var decision = Board(room!).Decisions.FirstOrDefault(d => d.Id == decisionId);
        return decision is null
            ? (null, null, CoffeeActionResult.DecisionNotFound())
            : (room, decision, null);
    }

    private async Task<(Room? Room, CoffeeTopic? Topic, CoffeeActionResult? Error)>
        LoadTopicForWriteAsync(string shortCode, string userId, Guid topicId, CancellationToken ct)
    {
        var (room, error) = await LoadForParticipantAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return (null, null, error);
        }

        var topic = Board(room!).Topics.FirstOrDefault(t => t.Id == topicId);
        if (topic is null)
        {
            return (null, null, CoffeeActionResult.TopicNotFound());
        }

        if (topic.AuthorUserId != userId && !RoomAuthz.CanControl(room!, userId))
        {
            return (null, null, CoffeeActionResult.NotTopicAuthor());
        }

        return (room, topic, null);
    }

    /// <summary>Organiser-gated load, mutate the board, commit.</summary>
    private async Task<CoffeeActionResult> ControlAsync(
        string shortCode, string userId, Action<CoffeeBoard> mutate, CancellationToken ct)
    {
        var (room, error) = await LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        mutate(Board(room!));
        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>Renumbers topics so orders stay dense after a withdrawal.</summary>
    private static void Reorder(CoffeeBoard board)
    {
        var remaining = board.Topics.OrderBy(t => t.Order).ToList();
        for (var i = 0; i < remaining.Count; i++)
        {
            remaining[i].Order = i;
        }
    }

    private async Task<CoffeeActionResult> CommitAsync(Room room, string forUserId, CancellationToken ct)
    {
        var outcome = await _rooms.CommitAsync(room, null, ct);
        return Project(outcome, forUserId);
    }

    private static CoffeeActionResult Project(RoomOutcome outcome, string forUserId) =>
        outcome.Status switch
        {
            SessionActionStatus.Ok when outcome.Room is null =>
                new CoffeeActionResult(CoffeeActionStatus.Ok, null), // deleted — nothing to show
            SessionActionStatus.Ok => CoffeeActionResult.Ok(ToSnapshot(outcome.Room!, forUserId)),
            SessionActionStatus.SessionNotFound => CoffeeActionResult.NotFound(),
            SessionActionStatus.NotParticipant => CoffeeActionResult.NotParticipant(),
            SessionActionStatus.NotOrganiser => CoffeeActionResult.NotOrganiser(),
            SessionActionStatus.SessionClosed => CoffeeActionResult.Closed(),
            _ => CoffeeActionResult.NotFound(),
        };

    // --- Projection --------------------------------------------------------

    /// <summary>
    /// Projects the board for one recipient — the single enforcement point for everything this tool
    /// hides, exactly as the retro's is (#21/#22/#23).
    /// <para>
    /// Three things are hidden here and nowhere else: other people's topics during Propose, dot
    /// totals while voting is open, and individual extension answers while that vote runs. Doing it
    /// in the one projection every read and broadcast passes through is what makes them properties
    /// of the wire; a client-side hide ships the data to every browser and hopes nobody looks.
    /// </para>
    /// </summary>
    public static CoffeeBoardSnapshot ToSnapshot(Room room, string forUserId)
    {
        var board = Board(room);
        var names = room.Participants.ToDictionary(p => p.UserId, p => p.DisplayName);
        var othersVisible = CoffeePhaseRules.OthersTopicsVisible(board.Phase);
        var totalsVisible = CoffeePhaseRules.VoteTotalsVisible(board.Phase);

        CoffeeTopicInfo ToInfo(CoffeeTopic t) => new(
            t.Id,
            t.Text,
            t.AuthorUserId,
            names.GetValueOrDefault(t.AuthorUserId, string.Empty),
            t.AuthorUserId == forUserId,
            t.Order,
            totalsVisible ? DotBudget.TotalOn(board.Votes, t.Id) : null,
            DotBudget.MineOn(board.Votes, forUserId, t.Id),
            t.IsDiscussed,
            t.DiscussedSeconds,
            t.Extensions);

        var visible = board.Topics
            .Where(t => othersVisible || t.AuthorUserId == forUserId)
            .OrderBy(t => t.Order)
            .Select(ToInfo)
            .ToArray();

        var hidden = othersVisible
            ? 0
            : board.Topics.Count(t => t.AuthorUserId != forUserId);

        // The agenda is a ranking, and a ranking is a total by another name — so it stays empty
        // until totals are visible, for the same reason the retro's does (#25).
        var agenda = totalsVisible
            ? Agenda(board).Select(ToInfo).ToArray()
            : Array.Empty<CoffeeTopicInfo>();

        CoffeeExtendVoteInfo? extend = null;
        if (board.CurrentTopicId is { } current && (board.ExtendVoteOpen || board.ExtendVotes.Count > 0))
        {
            var resolved = !board.ExtendVoteOpen;
            extend = new CoffeeExtendVoteInfo(
                current,
                board.ExtendVotes.Count,
                board.ExtendVotes.FirstOrDefault(v => v.VoterUserId == forUserId)?.Choice,
                resolved ? board.ExtendVotes.Count(v => v.Choice == ExtendChoice.KeepGoing) : null,
                resolved ? board.ExtendVotes.Count(v => v.Choice == ExtendChoice.MoveOn) : null);
        }

        var decisions = board.Decisions
            .OrderBy(d => d.IsDone)
            .ThenBy(d => d.DueDate ?? DateTimeOffset.MaxValue)
            .ThenBy(d => d.CreatedAt)
            .Select(d => new CoffeeDecisionInfo(
                d.Id, d.Title, d.TopicId, d.OwnerUserId, d.OwnerName, d.DueDate, d.IsDone, d.CreatedAt))
            .ToArray();

        return new CoffeeBoardSnapshot(
            RoomProjection.ToSnapshot(room, RoomProjection.ToInfos(room, revealed: false, [])),
            board.Phase,
            CoffeePhaseRules.Next(board.Phase),
            CoffeePhaseRules.Previous(board.Phase),
            board.PhaseDurationSeconds,
            board.PhaseDeadline,
            board.VoteBudget,
            board.AllowMultiplePerItem,
            DotBudget.RemainingFor(board.Votes, board.VoteBudget, forUserId),
            totalsVisible,
            visible,
            hidden,
            agenda,
            board.CurrentTopicId,
            extend,
            decisions);
    }
}
