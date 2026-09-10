using TeamTools.Core.Contracts;
using TeamTools.Core.Models;

namespace TeamTools.Core.Retro;

/// <summary>
/// Team Retro decision-making (#21): the board layout and the cards on it — add, edit, delete and
/// move — plus creation and the room operations projected as retro results.
/// <para>
/// Room concerns (join, leave, presence, roles, organisers, password, close/delete) are delegated
/// to <see cref="RoomService"/>, exactly as <c>PokerService</c> does, so both tools inherit one
/// implementation of everything room-level. Pure of transport/EF concerns, so it is unit-testable
/// against an in-memory <see cref="IRoomStore"/>.
/// </para>
/// </summary>
public class RetroService
{
    /// <summary>Longest a card may be. Long enough for a thought, short enough to read on a board.</summary>
    public const int MaxCardLength = 500;

    private readonly IRoomStore _store;
    private readonly RoomService _rooms;
    private readonly IClock _clock;

    public RetroService(IRoomStore store, RoomService rooms, IClock clock)
    {
        _store = store;
        _rooms = rooms;
        _clock = clock;
    }

    // --- Creation ----------------------------------------------------------

    public async Task<CreateRetroResult> CreateAsync(CreateRetroRequest request, CancellationToken ct = default)
    {
        if (NameNormalizer.IsBlank(request.Name))
        {
            return CreateRetroResult.InvalidName("Board name is required.");
        }

        if (NameNormalizer.IsBlank(request.CreatorDisplayName))
        {
            return CreateRetroResult.InvalidName("Your display name is required.");
        }

        IReadOnlyList<string> columnTitles;
        try
        {
            columnTitles = RetroTemplateCatalog.GetColumns(request.Template, request.CustomColumns);
        }
        catch (ArgumentException ex)
        {
            return CreateRetroResult.InvalidTemplate(ex.Message);
        }

        var room = await _rooms.NewRoomAsync(
            RoomTool.Retro, request.Name, request.CreatorUserId, request.CreatorDisplayName,
            request.Organise, request.EnableReactions, request.Password, ct);

        var board = new RetroBoard
        {
            RoomId = room.Id,
            Template = request.Template,
            Anonymous = request.Anonymous,
        };

        // Columns are materialised now rather than resolved per read: a card belongs to a column, so
        // the column needs an identity that survives a rename or a reordering.
        for (var i = 0; i < columnTitles.Count; i++)
        {
            board.Columns.Add(new RetroColumn
            {
                Id = Guid.NewGuid(),
                BoardId = room.Id,
                Title = columnTitles[i],
                Order = i,
            });
        }

        room.RetroBoard = board;
        await _rooms.AddAsync(room, ct);
        return CreateRetroResult.Ok(ToSnapshot(room, request.CreatorUserId));
    }

    // --- Room-level operations, projected as retro results -----------------

    public async Task<RetroJoinResult> JoinAsync(JoinSessionRequest request, CancellationToken ct = default)
    {
        var outcome = await _rooms.JoinAsync(request, ct);
        if (outcome.Status != JoinStatus.Ok)
        {
            return new RetroJoinResult(outcome.Status, null, null, outcome.Error);
        }

        var room = outcome.Room!;
        return RetroJoinResult.Ok(
            ToSnapshot(room, request.UserId),
            RoomProjection.ToInfo(outcome.Participant!, revealed: false));
    }

    public async Task<RetroActionResult> LeaveAsync(string shortCode, string userId, CancellationToken ct = default)
    {
        var (status, room) = await _rooms.LeaveAsync(shortCode, userId, ct);
        return status == LeaveStatus.Ok
            ? RetroActionResult.Ok(ToSnapshot(room!, userId))
            : RetroActionResult.NotFound();
    }

    public async Task<RetroActionResult> MarkDisconnectedAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.MarkDisconnectedAsync(shortCode, userId, ct), userId);

    public async Task<RetroActionResult> ChangeRoleAsync(
        string shortCode, string actingUserId, string targetUserId, ParticipantRole role,
        CancellationToken ct = default) =>
        Project(await _rooms.ChangeRoleAsync(shortCode, actingUserId, targetUserId, role, ct: ct), actingUserId);

    public async Task<RetroActionResult> SetAllowRoleChangeAsync(
        string shortCode, string userId, bool enabled, CancellationToken ct = default) =>
        Project(await _rooms.SetAllowRoleChangeAsync(shortCode, userId, enabled, ct), userId);

    public async Task<RetroActionResult> SetReactionsEnabledAsync(
        string shortCode, string userId, bool enabled, CancellationToken ct = default) =>
        Project(await _rooms.SetReactionsEnabledAsync(shortCode, userId, enabled, ct), userId);

    public Task<bool> AreReactionsEnabledAsync(string shortCode, CancellationToken ct = default) =>
        _rooms.AreReactionsEnabledAsync(shortCode, ct);

    public async Task<RetroActionResult> PromoteToOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.PromoteToOrganiserAsync(shortCode, actingUserId, targetUserId, ct), actingUserId);

    public async Task<RetroActionResult> DemoteOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.DemoteOrganiserAsync(shortCode, actingUserId, targetUserId, ct), actingUserId);

    public async Task<RetroActionResult> TransferOrganiserAsync(
        string shortCode, string actingUserId, string targetUserId, CancellationToken ct = default) =>
        Project(await _rooms.TransferOrganiserAsync(shortCode, actingUserId, targetUserId, ct), actingUserId);

    public async Task<RetroActionResult> SetPasswordAsync(
        string shortCode, string userId, string? newPassword, CancellationToken ct = default) =>
        Project(await _rooms.SetPasswordAsync(shortCode, userId, newPassword, ct), userId);

    public async Task<RetroActionResult> CloseBoardAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.CloseRoomAsync(shortCode, userId, ct: ct), userId);

    public async Task<RetroActionResult> DeleteBoardAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        Project(await _rooms.DeleteRoomAsync(shortCode, userId, ct), userId);

    // --- Cards -------------------------------------------------------------

    /// <summary>
    /// Adds a card to a column. Any participant may add one — a retro where only the facilitator
    /// writes is not a retro. Observers are the exception: they watch (see #21 role note).
    /// </summary>
    public async Task<RetroActionResult> AddCardAsync(
        string shortCode, string userId, Guid columnId, string text, CancellationToken ct = default)
    {
        var (room, error) = await LoadForParticipantAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (!RetroPhaseRules.CardsWritable(board.Phase))
        {
            // Past Collect, a new card would invalidate the grouping and tallies built on top of
            // the ones already there. See #23.
            return RetroActionResult.WrongPhase();
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxCardLength)
        {
            return RetroActionResult.InvalidCardText();
        }

        var column = board.Columns.FirstOrDefault(c => c.Id == columnId);
        if (column is null)
        {
            return RetroActionResult.ColumnNotFound();
        }

        var nextOrder = board.Cards.Count(c => c.ColumnId == columnId);
        board.Cards.Add(new RetroCard
        {
            Id = Guid.NewGuid(),
            BoardId = board.RoomId,
            ColumnId = columnId,
            AuthorUserId = userId,
            Text = trimmed,
            CreatedAt = _clock.UtcNow,
            Order = nextOrder,
        });

        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>Edits a card's text. Author or organiser only.</summary>
    public async Task<RetroActionResult> EditCardAsync(
        string shortCode, string userId, Guid cardId, string text, CancellationToken ct = default)
    {
        var (room, card, error) = await LoadCardForWriteAsync(shortCode, userId, cardId, ct);
        if (error is not null)
        {
            return error;
        }

        if (!RetroPhaseRules.CardsWritable(Board(room!).Phase))
        {
            return RetroActionResult.WrongPhase();
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxCardLength)
        {
            return RetroActionResult.InvalidCardText();
        }

        card!.Text = trimmed;
        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>Deletes a card. Author or organiser only; remaining cards close the gap.</summary>
    public async Task<RetroActionResult> DeleteCardAsync(
        string shortCode, string userId, Guid cardId, CancellationToken ct = default)
    {
        var (room, card, error) = await LoadCardForWriteAsync(shortCode, userId, cardId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        var columnId = card!.ColumnId;
        board.Cards.Remove(card);
        Reorder(board, columnId);

        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>
    /// Moves a card to another column (or reorders it within one). Author or organiser only — the
    /// same rule as editing, because moving a card changes what it means.
    /// </summary>
    public async Task<RetroActionResult> MoveCardAsync(
        string shortCode, string userId, Guid cardId, Guid targetColumnId, int targetOrder,
        CancellationToken ct = default)
    {
        var (room, card, error) = await LoadCardForWriteAsync(shortCode, userId, cardId, ct);
        if (error is not null)
        {
            return error;
        }

        var board = Board(room!);
        if (board.Columns.All(c => c.Id != targetColumnId))
        {
            return RetroActionResult.ColumnNotFound();
        }

        var sourceColumnId = card!.ColumnId;
        card.ColumnId = targetColumnId;

        // Place the card at the requested index among the target column's other cards, then
        // renumber both columns so the orders stay dense and gap-free.
        var others = board.Cards
            .Where(c => c.ColumnId == targetColumnId && c.Id != card.Id)
            .OrderBy(c => c.Order)
            .ToList();
        var index = Math.Clamp(targetOrder, 0, others.Count);
        others.Insert(index, card);
        for (var i = 0; i < others.Count; i++)
        {
            others[i].Order = i;
        }

        if (sourceColumnId != targetColumnId)
        {
            Reorder(board, sourceColumnId);
        }

        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>
    /// Organiser-only: swap the column layout. Rejected once cards exist — the cards belong to
    /// columns, and silently rehoming or dropping them is worse than making the organiser clear the
    /// board first.
    /// </summary>
    public async Task<RetroActionResult> SetTemplateAsync(
        string shortCode, string userId, RetroTemplate template, string? customColumns,
        CancellationToken ct = default)
    {
        var (room, error) = await _rooms.LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return Project(error, userId);
        }

        var board = Board(room!);
        if (board.Cards.Count > 0)
        {
            return RetroActionResult.InvalidTemplate();
        }

        IReadOnlyList<string> titles;
        try
        {
            titles = RetroTemplateCatalog.GetColumns(template, customColumns);
        }
        catch (ArgumentException)
        {
            return RetroActionResult.InvalidTemplate();
        }

        board.Template = template;
        board.Columns.Clear();
        for (var i = 0; i < titles.Count; i++)
        {
            board.Columns.Add(new RetroColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.RoomId,
                Title = titles[i],
                Order = i,
            });
        }

        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>
    /// Organiser-only: switch the board between attributed and anonymous cards (#22). Refused once
    /// any card exists — turning anonymity on would retroactively hide attributed cards, and turning
    /// it off would expose cards written under a promise of anonymity. Neither is ours to do.
    /// </summary>
    public async Task<RetroActionResult> SetAnonymousAsync(
        string shortCode, string userId, bool anonymous, CancellationToken ct = default)
    {
        var (room, error) = await _rooms.LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return Project(error, userId);
        }

        var board = Board(room!);
        if (board.Anonymous == anonymous)
        {
            return RetroActionResult.Ok(ToSnapshot(room!, userId)); // already there
        }

        if (board.Cards.Count > 0)
        {
            return RetroActionResult.AnonymityLocked();
        }

        board.Anonymous = anonymous;
        return await CommitAsync(room!, userId, ct);
    }

    // --- Phases (#23) ------------------------------------------------------

    /// <summary>
    /// Organiser-only: move the retro one phase forward. Optionally starts a countdown for the new
    /// phase (<paramref name="seconds"/>, or the configured duration) using the same deadline
    /// broadcast the poker round timer established (#14).
    /// </summary>
    public Task<RetroActionResult> AdvancePhaseAsync(
        string shortCode, string userId, int? seconds = null, CancellationToken ct = default) =>
        MovePhaseAsync(shortCode, userId, forward: true, seconds, ct);

    /// <summary>
    /// Organiser-only: step the retro back one phase. Facilitators mis-click, and the alternative is
    /// a retro stuck in the wrong phase. One step only — arbitrary jumps are refused.
    /// </summary>
    public Task<RetroActionResult> PreviousPhaseAsync(
        string shortCode, string userId, CancellationToken ct = default) =>
        MovePhaseAsync(shortCode, userId, forward: false, seconds: null, ct);

    /// <summary>
    /// Organiser-only: move to a named phase. Still one step at a time — this exists so a client can
    /// say where it thinks it is going rather than relying on the server's idea of "next", and it
    /// rejects anything that is not adjacent.
    /// </summary>
    public async Task<RetroActionResult> SetPhaseAsync(
        string shortCode, string userId, RetroPhase phase, int? seconds = null,
        CancellationToken ct = default)
    {
        var (room, error) = await _rooms.LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return Project(error, userId);
        }

        var board = Board(room!);
        if (!RetroPhaseRules.IsLegalTransition(board.Phase, phase))
        {
            return RetroActionResult.IllegalPhaseTransition();
        }

        ApplyPhase(board, phase, seconds);
        return await CommitAsync(room!, userId, ct);
    }

    /// <summary>Organiser-only: set or clear the configured phase-countdown length. See #23.</summary>
    public async Task<RetroActionResult> SetPhaseDurationAsync(
        string shortCode, string userId, int? seconds, CancellationToken ct = default)
    {
        var (room, error) = await _rooms.LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return Project(error, userId);
        }

        Board(room!).PhaseDurationSeconds = RetroPhaseRules.NormalizeDuration(seconds);
        return await CommitAsync(room!, userId, ct);
    }

    private async Task<RetroActionResult> MovePhaseAsync(
        string shortCode, string userId, bool forward, int? seconds, CancellationToken ct)
    {
        var (room, error) = await _rooms.LoadForControlAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return Project(error, userId);
        }

        var board = Board(room!);
        var target = forward ? RetroPhaseRules.Next(board.Phase) : RetroPhaseRules.Previous(board.Phase);
        if (target is null)
        {
            // Already at one end of the order.
            return RetroActionResult.IllegalPhaseTransition();
        }

        ApplyPhase(board, target.Value, seconds);
        return await CommitAsync(room!, userId, ct);
    }

    private void ApplyPhase(RetroBoard board, RetroPhase phase, int? seconds)
    {
        board.Phase = phase;

        var duration = RetroPhaseRules.NormalizeDuration(seconds) ?? board.PhaseDurationSeconds;
        if (duration is not null && phase != RetroPhase.Closed)
        {
            board.PhaseDurationSeconds = duration;
            board.PhaseDeadline = _clock.UtcNow.AddSeconds(duration.Value);
        }
        else
        {
            // A new phase never inherits the old phase's running countdown.
            board.PhaseDeadline = null;
        }
    }

    // --- Reads -------------------------------------------------------------

    /// <summary>
    /// The board as <paramref name="forUserId"/> may see it. The recipient matters: the projection
    /// is what enforces anonymity (#22) and hidden collection (#23).
    /// </summary>
    public async Task<RetroBoardSnapshot?> GetByShortCodeAsync(
        string shortCode, string forUserId, CancellationToken ct = default)
    {
        var room = await _store.FindByShortCodeAsync(shortCode, ct);
        return room?.RetroBoard is null ? null : ToSnapshot(room, forUserId);
    }

    // --- Helpers -----------------------------------------------------------

    /// <summary>
    /// The room's board. A retro room always has one; a missing board means the room hosts another
    /// tool, which is a programming error rather than a user one.
    /// </summary>
    private static RetroBoard Board(Room room) =>
        room.RetroBoard ?? throw new InvalidOperationException(
            $"Room '{room.ShortCode}' hosts {room.Tool}, not Retro — it has no retro board.");

    private async Task<(Room? Room, RetroActionResult? Error)> LoadForParticipantAsync(
        string shortCode, string userId, CancellationToken ct)
    {
        var (room, error) = await _rooms.LoadForParticipantAsync(shortCode, userId, ct);
        return error is not null ? (null, Project(error, userId)) : (room, null);
    }

    /// <summary>Loads a card the caller is allowed to change: its author, or an organiser.</summary>
    private async Task<(Room? Room, RetroCard? Card, RetroActionResult? Error)> LoadCardForWriteAsync(
        string shortCode, string userId, Guid cardId, CancellationToken ct)
    {
        var (room, error) = await LoadForParticipantAsync(shortCode, userId, ct);
        if (error is not null)
        {
            return (null, null, error);
        }

        var card = Board(room!).Cards.FirstOrDefault(c => c.Id == cardId);
        if (card is null)
        {
            return (null, null, RetroActionResult.CardNotFound());
        }

        if (card.AuthorUserId != userId && !RoomAuthz.CanControl(room!, userId))
        {
            return (null, null, RetroActionResult.NotCardAuthor());
        }

        return (room, card, null);
    }

    /// <summary>Renumbers a column's cards so orders stay dense after a removal or a move out.</summary>
    private static void Reorder(RetroBoard board, Guid columnId)
    {
        var remaining = board.Cards
            .Where(c => c.ColumnId == columnId)
            .OrderBy(c => c.Order)
            .ToList();
        for (var i = 0; i < remaining.Count; i++)
        {
            remaining[i].Order = i;
        }
    }

    private async Task<RetroActionResult> CommitAsync(Room room, string forUserId, CancellationToken ct)
    {
        var outcome = await _rooms.CommitAsync(room, null, ct);
        return Project(outcome, forUserId);
    }

    /// <summary>Turns a room-level outcome into a retro result, projecting the board on success.</summary>
    private static RetroActionResult Project(RoomOutcome outcome, string forUserId) =>
        outcome.Status switch
        {
            SessionActionStatus.Ok when outcome.Room is null =>
                new RetroActionResult(RetroActionStatus.Ok, null), // deleted — nothing left to show
            SessionActionStatus.Ok => RetroActionResult.Ok(ToSnapshot(outcome.Room!, forUserId)),
            SessionActionStatus.SessionNotFound => RetroActionResult.NotFound(),
            SessionActionStatus.NotParticipant => RetroActionResult.NotParticipant(),
            SessionActionStatus.NotOrganiser => RetroActionResult.NotOrganiser(),
            SessionActionStatus.SessionClosed => RetroActionResult.Closed(),
            _ => RetroActionResult.NotFound(),
        };

    // --- Projection --------------------------------------------------------

    /// <summary>
    /// Projects the board for one recipient.
    /// <para>
    /// <b>This method is where anonymity lives (#22).</b> On an anonymous board no card carries
    /// authorship at all — not even to the card's own author, who is identified by
    /// <c>IsMine</c> instead. Doing it here, in the one projection every read and broadcast goes
    /// through, is what makes the guarantee hold for the hub, the join result and every mutation
    /// result alike; a client-side hide would be disproved by one devtools panel.
    /// </para>
    /// </summary>
    public static RetroBoardSnapshot ToSnapshot(Room room, string forUserId)
    {
        var board = Board(room);
        var names = room.Participants.ToDictionary(p => p.UserId, p => p.DisplayName);

        // Hidden collection (#23): during Collect a participant sees only their own cards, plus a
        // count of how many others exist. Filtering here — in the one projection every read and
        // broadcast passes through — is what makes it a property of the wire; a client-side hide
        // would ship everyone's words to every browser and hope nobody looked.
        var othersVisible = RetroPhaseRules.OthersCardsVisible(board.Phase);

        var columns = board.Columns
            .OrderBy(c => c.Order)
            .Select(c =>
            {
                var inColumn = board.Cards
                    .Where(card => card.ColumnId == c.Id)
                    .OrderBy(card => card.Order)
                    .ToList();
                var visible = othersVisible
                    ? inColumn
                    : inColumn.Where(card => card.AuthorUserId == forUserId).ToList();

                return new RetroColumnInfo(
                    c.Id,
                    c.Title,
                    c.Order,
                    visible.Select(card => ToCardInfo(card, forUserId, names, board.Anonymous)).ToArray(),
                    inColumn.Count - visible.Count);
            })
            .ToArray();

        return new RetroBoardSnapshot(
            RoomProjection.ToSnapshot(room, RoomProjection.ToInfos(room, revealed: false)),
            board.Template,
            board.Phase,
            RetroPhaseRules.Next(board.Phase),
            RetroPhaseRules.Previous(board.Phase),
            board.PhaseDurationSeconds,
            board.PhaseDeadline,
            board.Anonymous,
            board.Cards.Count == 0,
            columns);
    }

    private static RetroCardInfo ToCardInfo(
        RetroCard card, string forUserId, IReadOnlyDictionary<string, string> names, bool anonymous)
    {
        var isMine = card.AuthorUserId == forUserId;

        return new RetroCardInfo(
            card.Id,
            card.Text,
            // Null for everyone on an anonymous board, including the author: "everyone but you"
            // would still put a userId on the wire, and one leak is all it takes.
            anonymous ? null : card.AuthorUserId,
            anonymous ? null : names.GetValueOrDefault(card.AuthorUserId),
            isMine,
            card.Order,
            card.CreatedAt);
    }
}
