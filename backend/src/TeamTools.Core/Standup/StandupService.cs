using TeamTools.Core.Contracts;
using TeamTools.Core.Models;

namespace TeamTools.Core.Standup;

/// <summary>
/// Async Standup decision-making (#36): everyone answers the questions in their own time, and reads
/// everyone else's once they have.
/// <para>
/// The thinnest of the four tool services, and deliberately so. There is <b>no phase rail</b> (a
/// standup opens, people post, it closes), <b>no countdown</b> and therefore no background sweep,
/// and <b>no voting</b>. What it does need it inherits: <see cref="RoomService"/> for everything
/// room-level (#19), <see cref="ActionItemRules"/> for blocker owners, and the per-recipient
/// projection for its one rule.
/// </para>
/// <para>
/// <b>That rule is post-to-read</b>, and it lives in <see cref="ToSnapshot"/> — the same enforcement
/// point as retro anonymity (#22) and hidden collection (#23).
/// </para>
/// </summary>
public class StandupService
{
    /// <summary>Longest an answer may be. A standup answer, not a status report.</summary>
    public const int MaxAnswerLength = 1000;

    /// <summary>Longest a blocker may be.</summary>
    public const int MaxBlockerLength = 300;

    /// <summary>Longest a question may be, and the most a room may ask.</summary>
    public const int MaxQuestionLength = 200;
    public const int MaxQuestions = 6;

    private readonly IRoomStore _store;
    private readonly RoomService _rooms;
    private readonly IClock _clock;

    public StandupService(IRoomStore store, RoomService rooms, IClock clock)
    {
        _store = store;
        _rooms = rooms;
        _clock = clock;
    }

    // --- Creation ----------------------------------------------------------

    public async Task<CreateStandupResult> CreateAsync(
        CreateStandupRequest request, CancellationToken ct = default)
    {
        if (NameNormalizer.IsBlank(request.Name))
        {
            return CreateStandupResult.InvalidName("A name is required.");
        }

        if (NameNormalizer.IsBlank(request.CreatorDisplayName))
        {
            return CreateStandupResult.InvalidName("Your display name is required.");
        }

        // Carry-over is resolved *before* the room is created, so a wrong password or an unknown code
        // fails without leaving a half-made standup behind — the lesson #27 learned.
        IReadOnlyList<string>? carriedQuestions = null;
        List<StandupBlocker> carriedBlockers = [];
        if (!string.IsNullOrWhiteSpace(request.PreviousBoardShortCode))
        {
            var (previous, error) = await ResolveCarryOverAsync(
                request.PreviousBoardShortCode!, request.PreviousBoardPassword, ct);
            if (error is not null)
            {
                return error;
            }

            carriedQuestions = previous!.Questions.OrderBy(q => q.Order).Select(q => q.Text).ToList();
            carriedBlockers = previous.Blockers
                .Where(b => !b.IsResolved)
                .Select(b => new StandupBlocker
                {
                    Id = Guid.NewGuid(),
                    AuthorUserId = b.AuthorUserId,
                    Text = b.Text,
                    OwnerUserId = b.OwnerUserId,
                    OwnerName = b.OwnerName,
                    CreatedAt = _clock.UtcNow,
                    CarriedFromBoardId = b.BoardId,
                })
                .ToList();
        }

        // Explicit questions win over carried ones, which win over the defaults.
        var questions = Normalize(request.Questions) ?? Normalize(carriedQuestions)
            ?? StandupQuestions.Default;
        if (questions.Count == 0 || questions.Count > MaxQuestions)
        {
            return CreateStandupResult.InvalidQuestions(
                $"A standup asks between one and {MaxQuestions} questions.");
        }

        var room = await _rooms.NewRoomAsync(
            RoomTool.Standup, request.Name, request.CreatorUserId, request.CreatorDisplayName,
            request.Organise, request.EnableReactions, request.Password, ct);

        var board = new StandupBoard
        {
            RoomId = room.Id,
            PreviousBoardShortCode = request.PreviousBoardShortCode,
        };

        for (var i = 0; i < questions.Count; i++)
        {
            board.Questions.Add(new StandupQuestion
            {
                Id = Guid.NewGuid(),
                BoardId = room.Id,
                Text = questions[i],
                Order = i,
            });
        }

        foreach (var blocker in carriedBlockers)
        {
            blocker.BoardId = room.Id;
            board.Blockers.Add(blocker);
        }

        room.StandupBoard = board;
        await _rooms.AddAsync(room, ct);
        return CreateStandupResult.Ok(ToSnapshot(room, request.CreatorUserId));
    }

    /// <summary>
    /// The previous standup's board, or the failure to report.
    /// <para>
    /// <b>The password matters</b>, for the reason retro carry-over's does (#27): a short code is a
    /// bearer token, and without this check carrying forward would be a way to read a protected
    /// standup's blockers.
    /// </para>
    /// </summary>
    private async Task<(StandupBoard? Board, CreateStandupResult? Error)> ResolveCarryOverAsync(
        string shortCode, string? password, CancellationToken ct)
    {
        var previous = await _store.FindByShortCodeAsync(shortCode, ct);
        if (previous?.StandupBoard is null)
        {
            return (null, CreateStandupResult.PreviousNotFound());
        }

        if (!_rooms.VerifyPassword(previous, password))
        {
            return (null, CreateStandupResult.PreviousPasswordRequired());
        }

        return (previous.StandupBoard, null);
    }

    /// <summary>Trims, drops blanks and truncates; null when the caller supplied nothing usable.</summary>
    private static IReadOnlyList<string>? Normalize(IReadOnlyList<string>? questions)
    {
        if (questions is null)
        {
            return null;
        }

        var cleaned = questions
            .Select(q => (q ?? string.Empty).Trim())
            .Where(q => q.Length > 0)
            .Select(q => q[..Math.Min(q.Length, MaxQuestionLength)])
            .ToList();

        return cleaned.Count == 0 ? null : cleaned;
    }

    // --- Room-level operations, projected as standup results ---------------

    public async Task<StandupJoinResult> JoinAsync(
        JoinSessionRequest request, CancellationToken ct = default)
    {
        var outcome = await _rooms.JoinAsync(request, RoomTool.Standup, ct);
        if (outcome.Status != JoinStatus.Ok)
        {
            return new StandupJoinResult(outcome.Status, null, null, outcome.Error);
        }

        var room = outcome.Room!;
        return StandupJoinResult.Ok(
            ToSnapshot(room, request.UserId),
            RoomProjection.ToInfo(outcome.Participant!, revealed: false));
    }

    public async Task<StandupActionResult> LeaveAsync(
        string shortCode, string userId, CancellationToken ct = default)
    {
        var (status, room) = await _rooms.LeaveAsync(shortCode, userId, ct);
        return status == LeaveStatus.Ok
            ? StandupActionResult.Ok(ToSnapshot(room!, userId))
            : StandupActionResult.NotFound();
    }

    public async Task<StandupActionResult> MarkDisconnectedAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.MarkDisconnectedAsync(shortCode, userId, ct), userId);

    public async Task<StandupActionResult> SetReactionsEnabledAsync(
        string shortCode, string userId, bool enabled, CancellationToken ct = default) =>
        Project(await _rooms.SetReactionsEnabledAsync(shortCode, userId, enabled, ct), userId);

    public Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken ct = default) =>
        _rooms.AreReactionsEnabledAsync(shortCode, ct);

    public async Task<StandupActionResult> PromoteToOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.PromoteToOrganiserAsync(shortCode, actingUserId, targetUserId, ct), actingUserId);

    public async Task<StandupActionResult> TransferOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.TransferOrganiserAsync(shortCode, actingUserId, targetUserId, ct), actingUserId);

    public async Task<StandupActionResult> SetPasswordAsync(
        string shortCode, string userId, string? password, CancellationToken ct = default) =>
        Project(await _rooms.SetPasswordAsync(shortCode, userId, password, ct), userId);

    public async Task<StandupActionResult> CloseBoardAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.CloseRoomAsync(shortCode, userId, ct: ct), userId);

    public async Task<StandupActionResult> DeleteBoardAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.DeleteRoomAsync(shortCode, userId, ct), userId);

    // --- Answering ---------------------------------------------------------

    /// <summary>
    /// Saves this person's answer to one question — creating the row on first save and updating it
    /// after that, so "I have posted" stays a fact about rows rather than a flag to keep in sync.
    /// <para>
    /// An empty answer clears it. That is deliberate: someone who has nothing for a question should
    /// be able to say so without a placeholder, and clearing every answer honestly takes them back
    /// to not having posted.
    /// </para>
    /// </summary>
    public async Task<StandupActionResult> AnswerAsync(
        string shortCode, string userId, Guid questionId, string text, CancellationToken ct = default)
    {
        var (room, error) = await LoadForParticipantAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (board.Questions.All(q => q.Id != questionId))
        {
            return StandupActionResult.QuestionNotFound();
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length > MaxAnswerLength)
        {
            return StandupActionResult.InvalidAnswer();
        }

        var existing = board.Entries.FirstOrDefault(
            e => e.AuthorUserId == userId && e.QuestionId == questionId);

        if (existing is null)
        {
            if (trimmed.Length > 0)
            {
                board.Entries.Add(new StandupEntry
                {
                    Id = Guid.NewGuid(),
                    BoardId = board.RoomId,
                    QuestionId = questionId,
                    AuthorUserId = userId,
                    Text = trimmed,
                    CreatedAt = _clock.UtcNow,
                });
            }
        }
        else if (trimmed.Length == 0)
        {
            board.Entries.Remove(existing);
        }
        else
        {
            existing.Text = trimmed;
            existing.UpdatedAt = _clock.UtcNow;
        }

        return await CommitAsync(room!, userId, ct);
    }

    // --- Blockers ----------------------------------------------------------

    /// <summary>Raises something that is in this person's way.</summary>
    public async Task<StandupActionResult> AddBlockerAsync(
        string shortCode, string userId, string text, CancellationToken ct = default)
    {
        var (room, error) = await LoadForParticipantAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxBlockerLength)
        {
            return StandupActionResult.InvalidBlockerText();
        }

        Board(room!).Blockers.Add(new StandupBlocker
        {
            Id = Guid.NewGuid(),
            BoardId = Board(room!).RoomId,
            AuthorUserId = userId,
            Text = trimmed,
            CreatedAt = _clock.UtcNow,
        });

        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>
    /// Says who is unblocking this — the one decision a standup actually produces. Any participant
    /// may take one on; the owner may be someone who was never in the room
    /// (<see cref="ActionItemRules"/>).
    /// </summary>
    public async Task<StandupActionResult> AssignBlockerAsync(
        string shortCode, string userId, Guid blockerId, string? ownerUserId, string? ownerName,
        CancellationToken ct = default)
    {
        var (room, blocker, error) = await LoadBlockerAsync(shortCode, userId, blockerId, ct);
        if (error is not null)
        {
            return error;
        }

        blocker!.OwnerUserId = ownerUserId;
        blocker.OwnerName = ActionItemRules.ResolveOwnerName(room!, ownerUserId, ownerName);
        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>
    /// Clears a blocker, or puts it back. Works on a closed board, for the reason retro actions do
    /// (#26): it gets unblocked hours after the standup, not during it.
    /// </summary>
    public async Task<StandupActionResult> ToggleBlockerResolvedAsync(
        string shortCode, string userId, Guid blockerId, CancellationToken ct = default)
    {
        var (room, blocker, error) = await LoadBlockerAsync(shortCode, userId, blockerId, ct);
        if (error is not null)
        {
            return error;
        }

        blocker!.ResolvedAt = blocker.ResolvedAt is null ? _clock.UtcNow : null;
        return await CommitAsync(room!, userId, ct);
    }

    public async Task<StandupActionResult> DeleteBlockerAsync(
        string shortCode, string userId, Guid blockerId, CancellationToken ct = default)
    {
        var (room, blocker, error) = await LoadBlockerAsync(shortCode, userId, blockerId, ct);
        if (error is not null)
        {
            return error;
        }

        Board(room!).Blockers.Remove(blocker!);
        return await CommitAsync(room!, userId, ct);
    }

    // --- Reads -------------------------------------------------------------

    public async Task<StandupBoardSnapshot?> GetByShortCodeAsync(
        string shortCode, string forUserId, CancellationToken ct = default)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        return room?.StandupBoard is null ? null : ToSnapshot(room, forUserId);
    }

    // --- Helpers -----------------------------------------------------------

    private static StandupBoard Board(Room room) =>
        room.StandupBoard ?? throw new InvalidOperationException(
            $"Room '{room.ShortCode}' hosts {room.Tool}, not Standup — it has no standup board.");

    private async Task<(Room? Room, StandupActionResult? Error)> LoadForParticipantAsync(
        string shortCode, string userId, CancellationToken ct)
    {
        var (room, error) = await _rooms.LoadForParticipantAsync(shortCode, userId, ct);
        return error is not null ? (null, Project(error, userId)) : (room, null);
    }

    /// <summary>
    /// Loads a blocker for a write. Uses the closed-room carve-out (#26/#36): everything else on a
    /// closed standup is frozen, but a blocker gets cleared once someone has actually unblocked it.
    /// </summary>
    private async Task<(Room? Room, StandupBlocker? Blocker, StandupActionResult? Error)>
        LoadBlockerAsync(string shortCode, string userId, Guid blockerId, CancellationToken ct)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        if (room?.StandupBoard is null)
        {
            return (null, null, StandupActionResult.NotFound());
        }

        if (room.Participants.All(p => p.UserId != userId))
        {
            return (null, null, StandupActionResult.NotParticipant());
        }

        var blocker = room.StandupBoard.Blockers.FirstOrDefault(b => b.Id == blockerId);
        return blocker is null
            ? (null, null, StandupActionResult.BlockerNotFound())
            : (room, blocker, null);
    }

    private async Task<StandupActionResult> CommitAsync(
        Room room, string forUserId, CancellationToken ct)
    {
        var outcome = await _rooms.CommitAsync(room, null, ct);
        return Project(outcome, forUserId);
    }

    private static StandupActionResult Project(RoomOutcome outcome, string forUserId) =>
        outcome.Status switch
        {
            SessionActionStatus.Ok when outcome.Room is null =>
                new StandupActionResult(StandupActionStatus.Ok, null), // deleted
            SessionActionStatus.Ok => StandupActionResult.Ok(ToSnapshot(outcome.Room!, forUserId)),
            SessionActionStatus.SessionNotFound => StandupActionResult.NotFound(),
            SessionActionStatus.NotParticipant => StandupActionResult.NotParticipant(),
            SessionActionStatus.NotOrganiser => StandupActionResult.NotOrganiser(),
            SessionActionStatus.SessionClosed => StandupActionResult.Closed(),
            _ => StandupActionResult.NotFound(),
        };

    // --- Projection --------------------------------------------------------

    /// <summary>
    /// Projects the board for one recipient — the single enforcement point for this tool's one rule.
    /// <para>
    /// <b>Post-to-read (#36).</b> Until you have posted, you get your own answers and nobody else's.
    /// The same anti-anchoring principle as the retro's hidden collection (#23): a standup you read
    /// first is a standup you write to match. Enforced here, in the one projection every read and
    /// broadcast passes through, so it is a property of the wire rather than of the UI (#22).
    /// </para>
    /// <para>
    /// What is <em>always</em> sent is the count of how many people have posted — never a list of who
    /// has not. Presence only knows who opened the room, so "Dave hasn't posted" would as often mean
    /// "Dave is on holiday"; a roster needs accounts, which the platform does not have.
    /// </para>
    /// </summary>
    public static StandupBoardSnapshot ToSnapshot(Room room, string forUserId)
    {
        var board = Board(room);
        var iHavePosted = board.HasPosted(forUserId);

        var questions = board.Questions
            .OrderBy(q => q.Order)
            .Select(q => new StandupQuestionInfo(q.Id, q.Text, q.Order))
            .ToArray();

        var authors = board.Entries.Select(e => e.AuthorUserId).Distinct().ToHashSet();

        var people = room.Participants
            .Where(p => iHavePosted || p.UserId == forUserId)
            .Where(p => authors.Contains(p.UserId) || p.UserId == forUserId)
            .Select(p =>
            {
                var mine = board.Entries
                    .Where(e => e.AuthorUserId == p.UserId)
                    .ToList();
                var answers = questions
                    .Select(q => (q, entry: mine.FirstOrDefault(e => e.QuestionId == q.Id)))
                    .Where(x => x.entry is not null)
                    .Select(x => new StandupAnswerInfo(x.q.Id, x.entry!.Text, x.entry.UpdatedAt))
                    .ToArray();

                return new StandupPersonInfo(
                    p.UserId,
                    p.DisplayName,
                    p.UserId == forUserId,
                    answers,
                    mine.Count == 0 ? null : mine.Min(e => e.CreatedAt));
            })
            .OrderByDescending(p => p.IsMe)
            .ThenBy(p => p.DisplayName)
            .ToArray();

        var names = room.Participants.ToDictionary(p => p.UserId, p => p.DisplayName);
        var blockers = board.Blockers
            .OrderBy(b => b.IsResolved)
            .ThenBy(b => b.CreatedAt)
            .Select(b => new StandupBlockerInfo(
                b.Id,
                b.Text,
                b.AuthorUserId,
                names.GetValueOrDefault(b.AuthorUserId, string.Empty),
                b.OwnerUserId,
                b.OwnerName,
                b.IsResolved,
                b.CarriedFromBoardId is not null,
                b.CreatedAt))
            .ToArray();

        return new StandupBoardSnapshot(
            RoomProjection.ToSnapshot(room, RoomProjection.ToInfos(room, revealed: false, [])),
            questions,
            iHavePosted,
            authors.Count,
            room.Participants.Count,
            people,
            blockers,
            board.PreviousBoardShortCode);
    }
}
