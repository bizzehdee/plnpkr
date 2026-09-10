using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamTools.Core.Poker;
using TeamTools.Core.Retro;
using TeamTools.Core;
using TeamTools.Core.Models;
using TeamTools.Data;
using TeamTools.Data.Sqlite;
using Xunit;

namespace TeamTools.Data.Tests;

/// <summary>
/// Verifies <see cref="EfRoomStore"/> against a real SQLite database (a shared in-memory
/// connection), confirming it honours the contract PokerService relies on. See #16.
/// </summary>
public sealed class EfRoomStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public EfRoomStoreTests()
    {
        // A single open in-memory connection keeps the schema alive for the test's lifetime.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    private TeamToolsDbContext NewContext()
    {
        // SQLite migrations now live in their own assembly (#19); point EF at it.
        var options = new DbContextOptionsBuilder<TeamToolsDbContext>()
            .UseSqlite(_connection, o => o.MigrationsAssembly(typeof(SqliteDatabaseProvider).Assembly.GetName().Name))
            .Options;
        return new TeamToolsDbContext(options);
    }

    [Fact]
    public async Task Can_persist_a_round_result()
    {
        var store = new EfRoomStore(NewContext());
        var room = NewRoom("rr-1");
        await store.AddAsync(room);

        room.PokerRound!.RoundResults.Add(new RoundResult
        {
            Story = "S",
            FinalEstimate = "5",
            Average = 5,
            Consensus = true,
            VoteCount = 2,
            RecordedAt = DateTimeOffset.UnixEpoch,
        });
        await store.UpdateAsync(room);

        var reloaded = await new EfRoomStore(NewContext()).FindByShortCodeAsync("rr-1");
        reloaded!.PokerRound!.RoundResults.Should().ContainSingle().Which.FinalEstimate.Should().Be("5");
    }

    private static Room NewRoom(string shortCode, string organiserUserId = "u1", string name = "Alice") => new()
    {
        Id = Guid.NewGuid(),
        ShortCode = shortCode,
        Name = "Sprint",
        Tool = RoomTool.Poker,
        OrganiserUserId = organiserUserId,
        PokerRound = new PokerRound { DeckType = DeckType.Fibonacci, State = SessionState.Voting },
        CreatedAt = DateTimeOffset.UnixEpoch,
        LastActivityAt = DateTimeOffset.UnixEpoch,
        Participants =
        {
            new Participant
            {
                UserId = organiserUserId,
                DisplayName = name,
                NormalizedName = name.ToLowerInvariant(),
                IsOrganiser = true,
                Role = ParticipantRole.Observer,
                IsConnected = true,
            },
        },
    };

    [Fact]
    public async Task Add_then_find_round_trips_the_session_with_participants()
    {
        await new EfRoomStore(NewContext()).AddAsync(NewRoom("blue-fox-42"));

        var loaded = await new EfRoomStore(NewContext()).FindByShortCodeAsync("blue-fox-42");

        loaded.Should().NotBeNull();
        loaded!.Participants.Should().ContainSingle().Which.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public async Task ShortCodeExists_reflects_stored_sessions()
    {
        await new EfRoomStore(NewContext()).AddAsync(NewRoom("blue-fox-42"));

        var store = new EfRoomStore(NewContext());
        (await store.ShortCodeExistsAsync("blue-fox-42")).Should().BeTrue();
        (await store.ShortCodeExistsAsync("nope-nope-9")).Should().BeFalse();
    }

    [Fact]
    public async Task Soft_deleted_sessions_are_hidden_from_every_read()
    {
        await new EfRoomStore(NewContext()).AddAsync(NewRoom("blue-fox-42"));

        // Soft-delete it (#26): set DeletedAt + persist.
        var deleting = new EfRoomStore(NewContext());
        var room = await deleting.FindByShortCodeAsync("blue-fox-42");
        room!.DeletedAt = DateTimeOffset.UtcNow;
        await deleting.UpdateAsync(room);

        // The global query filter now hides it from find / exists / get-all.
        var store = new EfRoomStore(NewContext());
        (await store.FindByShortCodeAsync("blue-fox-42")).Should().BeNull();
        (await store.ShortCodeExistsAsync("blue-fox-42")).Should().BeFalse();
        (await store.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_name_within_a_session_throws_DuplicateNameException()
    {
        await new EfRoomStore(NewContext()).AddAsync(NewRoom("blue-fox-42"));

        var store = new EfRoomStore(NewContext());
        var room = await store.FindByShortCodeAsync("blue-fox-42");
        room!.Participants.Add(new Participant
        {
            RoomId = room.Id,
            UserId = "different-user",
            DisplayName = "ALICE",
            NormalizedName = "alice", // collides with the existing participant
            Role = ParticipantRole.Voter,
        });

        var act = () => store.UpdateAsync(room);

        await act.Should().ThrowAsync<DuplicateNameException>();
    }

    [Fact]
    public async Task Remove_deletes_the_session_and_cascades_to_participants()
    {
        await new EfRoomStore(NewContext()).AddAsync(NewRoom("blue-fox-42"));

        var store = new EfRoomStore(NewContext());
        var room = await store.FindByShortCodeAsync("blue-fox-42");
        await store.RemoveAsync(room!);

        (await new EfRoomStore(NewContext()).FindByShortCodeAsync("blue-fox-42")).Should().BeNull();
        using var ctx = NewContext();
        (await ctx.Participants.CountAsync()).Should().Be(0); // cascade delete
    }

    [Fact]
    public async Task Remove_cascades_to_round_results()
    {
        var room = NewRoom("blue-fox-42");
        await new EfRoomStore(NewContext()).AddAsync(room);

        var adding = new EfRoomStore(NewContext());
        var loaded = await adding.FindByShortCodeAsync("blue-fox-42");
        loaded!.PokerRound!.RoundResults.Add(new RoundResult
        {
            Story = "S",
            FinalEstimate = "5",
            Average = 5,
            Consensus = true,
            VoteCount = 2,
            RecordedAt = DateTimeOffset.UnixEpoch,
        });
        await adding.UpdateAsync(loaded);

        var store = new EfRoomStore(NewContext());
        await store.RemoveAsync((await store.FindByShortCodeAsync("blue-fox-42"))!);

        using var ctx = NewContext();
        (await ctx.Set<RoundResult>().CountAsync()).Should().Be(0); // cascade delete (#15)
    }

    [Fact]
    public async Task GetSoftDeletedPastRetention_finds_only_soft_deleted_sessions_at_or_before_the_threshold()
    {
        var now = DateTimeOffset.UtcNow;

        // Soft-deleted well past the threshold — a hard-delete candidate.
        var pastDue = NewRoom("past-due-1", "u1");
        await new EfRoomStore(NewContext()).AddAsync(pastDue);
        var deletingPastDue = new EfRoomStore(NewContext());
        var loadedPastDue = await deletingPastDue.FindByShortCodeAsync("past-due-1");
        loadedPastDue!.DeletedAt = now.AddDays(-31);
        await deletingPastDue.UpdateAsync(loadedPastDue);

        // Soft-deleted, but not yet past the threshold.
        var recent = NewRoom("recent-1", "u2", "Bob");
        await new EfRoomStore(NewContext()).AddAsync(recent);
        var deletingRecent = new EfRoomStore(NewContext());
        var loadedRecent = await deletingRecent.FindByShortCodeAsync("recent-1");
        loadedRecent!.DeletedAt = now.AddDays(-1);
        await deletingRecent.UpdateAsync(loadedRecent);

        // Never soft-deleted at all.
        await new EfRoomStore(NewContext()).AddAsync(NewRoom("still-alive-1", "u3", "Cara"));

        var result = await new EfRoomStore(NewContext()).GetSoftDeletedPastRetentionAsync(now.AddDays(-30));

        result.Should().ContainSingle().Which.ShortCode.Should().Be("past-due-1");
    }

    [Fact]
    public async Task GetAll_returns_all_sessions_with_participants()
    {
        await new EfRoomStore(NewContext()).AddAsync(NewRoom("blue-fox-42", "u1"));
        await new EfRoomStore(NewContext()).AddAsync(NewRoom("red-owl-99", "u2", "Bob"));

        var all = await new EfRoomStore(NewContext()).GetAllAsync();

        all.Should().HaveCount(2);
        all.SelectMany(s => s.Participants).Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRoomsWithExpiredTimer_returns_only_due_voting_rooms()
    {
        var now = DateTimeOffset.UtcNow;

        // Due: Voting with a deadline in the past.
        var due = NewRoom("due-timer-1", "u1");
        due.PokerRound!.TimerDeadline = now.AddSeconds(-1);
        await new EfRoomStore(NewContext()).AddAsync(due);

        // Not due: deadline still in the future.
        var future = NewRoom("future-timer-1", "u2", "Bob");
        future.PokerRound!.TimerDeadline = now.AddMinutes(5);
        await new EfRoomStore(NewContext()).AddAsync(future);

        // Not due: no timer running.
        await new EfRoomStore(NewContext()).AddAsync(NewRoom("no-timer-1", "u3", "Cara"));

        // Not due: already revealed (timer was running but the round is over).
        var revealed = NewRoom("revealed-timer-1", "u4", "Dan");
        revealed.PokerRound!.State = SessionState.Revealed;
        revealed.PokerRound!.TimerDeadline = now.AddSeconds(-10);
        await new EfRoomStore(NewContext()).AddAsync(revealed);

        var result = await new EfRoomStore(NewContext()).GetRoomsWithExpiredTimerAsync(now);

        result.Should().ContainSingle().Which.ShortCode.Should().Be("due-timer-1");
        result[0].Participants.Should().ContainSingle(); // participants are included for the snapshot
    }

    // --- The retro phase-countdown sweep (#33) ----------------------------

    /// <summary>A retro room with one column, one card and one participant.</summary>
    private static Room NewRetroRoom(string shortCode, DateTimeOffset? phaseDeadline)
    {
        var boardId = Guid.NewGuid();
        return new Room
        {
            Id = boardId,
            ShortCode = shortCode,
            Name = "Retro",
            Tool = RoomTool.Retro,
            OrganiserUserId = "u1",
            CreatedAt = DateTimeOffset.UnixEpoch,
            LastActivityAt = DateTimeOffset.UnixEpoch,
            RetroBoard = new RetroBoard
            {
                RoomId = boardId,
                Template = RetroTemplate.MadSadGlad,
                Phase = RetroPhase.Collect,
                PhaseDeadline = phaseDeadline,
                Columns = { new RetroColumn { Id = Guid.NewGuid(), BoardId = boardId, Title = "Mad", Order = 0 } },
            },
            Participants =
            {
                new Participant
                {
                    UserId = "u1",
                    DisplayName = "Alice",
                    NormalizedName = "alice",
                    IsOrganiser = true,
                    Role = ParticipantRole.Voter,
                    IsConnected = true,
                },
            },
        };
    }

    [Fact]
    public async Task GetRoomsWithExpiredPhase_returns_only_boards_whose_countdown_has_elapsed()
    {
        var now = DateTimeOffset.UtcNow;

        await new EfRoomStore(NewContext()).AddAsync(NewRetroRoom("due-phase-1", now.AddSeconds(-1)));
        await new EfRoomStore(NewContext()).AddAsync(NewRetroRoom("future-phase-1", now.AddMinutes(5)));
        await new EfRoomStore(NewContext()).AddAsync(NewRetroRoom("no-phase-1", null));

        var result = await new EfRoomStore(NewContext()).GetRoomsWithExpiredPhaseAsync(now);

        result.Should().ContainSingle().Which.ShortCode.Should().Be("due-phase-1");
        result[0].RetroBoard.Should().NotBeNull("the caller clears the deadline on it");
    }

    [Fact]
    public async Task GetRoomsWithExpiredPhase_ignores_poker_rooms()
    {
        // The sweep runs once per second against a database that may hold nothing but poker rooms.
        var now = DateTimeOffset.UtcNow;
        var poker = NewRoom("poker-phase-1", "u9");
        poker.PokerRound!.TimerDeadline = now.AddSeconds(-30);
        await new EfRoomStore(NewContext()).AddAsync(poker);

        var result = await new EfRoomStore(NewContext()).GetRoomsWithExpiredPhaseAsync(now);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoomsWithExpiredPhase_does_not_load_the_board_collections()
    {
        // The point of #33: this replaced GetAllAsync, which pulled every room in the database with
        // its participants, round history and the whole retro board graph — once a second, forever.
        // Asserting on what is *not* loaded is the only way to keep that from creeping back.
        var now = DateTimeOffset.UtcNow;
        await new EfRoomStore(NewContext()).AddAsync(NewRetroRoom("lean-phase-1", now.AddSeconds(-1)));

        var result = await new EfRoomStore(NewContext()).GetRoomsWithExpiredPhaseAsync(now);

        var room = result.Should().ContainSingle().Subject;
        room.RetroBoard!.Columns.Should().BeEmpty("columns are not included — the board is re-read to broadcast");
        room.Participants.Should().BeEmpty("nor are participants");
    }

    [Fact]
    public async Task GetRoomsWithExpiredPhase_returns_tracked_entities_so_clearing_the_deadline_persists()
    {
        // The sweep's whole job is to clear the deadline and save. A leaner Include must not become
        // an AsNoTracking read, or the countdown would fire every second forever.
        var now = DateTimeOffset.UtcNow;
        await new EfRoomStore(NewContext()).AddAsync(NewRetroRoom("tracked-phase-1", now.AddSeconds(-1)));

        var store = new EfRoomStore(NewContext());
        var room = (await store.GetRoomsWithExpiredPhaseAsync(now)).Single();
        room.RetroBoard!.PhaseDeadline = null;
        await store.UpdateAsync(room);

        var reloaded = await new EfRoomStore(NewContext()).FindByShortCodeAsync("tracked-phase-1");
        reloaded!.RetroBoard!.PhaseDeadline.Should().BeNull();
        (await new EfRoomStore(NewContext()).GetRoomsWithExpiredPhaseAsync(now)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoomsWithExpiredPhase_skips_a_soft_deleted_room()
    {
        // The global query filter, not an explicit clause — but worth pinning: an evicted retro must
        // not keep the sweep busy.
        var now = DateTimeOffset.UtcNow;
        var deleted = NewRetroRoom("deleted-phase-1", now.AddSeconds(-1));
        deleted.DeletedAt = now.AddDays(-1);
        await new EfRoomStore(NewContext()).AddAsync(deleted);

        var result = await new EfRoomStore(NewContext()).GetRoomsWithExpiredPhaseAsync(now);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AreReactionsEnabled_reflects_the_flag_and_excludes_missing_or_deleted()
    {
        var on = NewRoom("reactions-on-1", "u1");
        on.ReactionsEnabled = true;
        await new EfRoomStore(NewContext()).AddAsync(on);

        var off = NewRoom("reactions-off-1", "u2", "Bob");
        off.ReactionsEnabled = false;
        await new EfRoomStore(NewContext()).AddAsync(off);

        var store = new EfRoomStore(NewContext());
        (await store.AreReactionsEnabledAsync("reactions-on-1")).Should().BeTrue();
        (await store.AreReactionsEnabledAsync("reactions-off-1")).Should().BeFalse();
        (await store.AreReactionsEnabledAsync("nope-nope-9")).Should().BeFalse();
    }

    // --- Retro boards (#21) -------------------------------------------------

    private static Room NewRetroRoom(string shortCode)
    {
        var roomId = Guid.NewGuid();
        return new Room
        {
            Id = roomId,
            ShortCode = shortCode,
            Name = "Sprint 24 retro",
            Tool = RoomTool.Retro,
            OrganiserUserId = "u1",
            CreatedAt = DateTimeOffset.UnixEpoch,
            LastActivityAt = DateTimeOffset.UnixEpoch,
            Participants =
            {
                new Participant
                {
                    UserId = "u1",
                    DisplayName = "Alice",
                    NormalizedName = "alice",
                    IsOrganiser = true,
                    Role = ParticipantRole.Observer,
                    IsConnected = true,
                },
            },
            RetroBoard = new RetroBoard
            {
                RoomId = roomId,
                Template = RetroTemplate.WentWellToImprove,
                Columns =
                {
                    new RetroColumn { Id = Guid.NewGuid(), BoardId = roomId, Title = "Went well", Order = 0 },
                    new RetroColumn { Id = Guid.NewGuid(), BoardId = roomId, Title = "To improve", Order = 1 },
                },
            },
        };
    }

    [Fact]
    public async Task Add_then_find_round_trips_a_retro_board_with_its_columns()
    {
        await new EfRoomStore(NewContext()).AddAsync(NewRetroRoom("retro-1"));

        var loaded = await new EfRoomStore(NewContext()).FindByShortCodeAsync("retro-1");

        loaded!.Tool.Should().Be(RoomTool.Retro);
        loaded.PokerRound.Should().BeNull("a retro room has no poker payload");
        loaded.RetroBoard!.Columns.OrderBy(c => c.Order).Select(c => c.Title)
            .Should().Equal("Went well", "To improve");
    }

    [Fact]
    public async Task A_card_added_to_an_already_loaded_board_is_inserted()
    {
        // Regression: the domain assigns card ids, and without ValueGeneratedNever EF reads a set
        // Guid key on an entity added to an already-tracked room as proof the row exists — issuing
        // an UPDATE that matches nothing and throwing DbUpdateConcurrencyException. Creation hid it,
        // because a brand-new graph is Added wholesale. Only a real database shows this.
        var room = NewRetroRoom("retro-2");
        await new EfRoomStore(NewContext()).AddAsync(room);

        var store = new EfRoomStore(NewContext());
        var loaded = await store.FindByShortCodeAsync("retro-2");
        var columnId = loaded!.RetroBoard!.Columns.First().Id;
        loaded.RetroBoard.Cards.Add(new RetroCard
        {
            Id = Guid.NewGuid(),
            BoardId = loaded.Id,
            ColumnId = columnId,
            AuthorUserId = "u1",
            Text = "Deploys got faster",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Order = 0,
        });

        await store.UpdateAsync(loaded);

        var reloaded = await new EfRoomStore(NewContext()).FindByShortCodeAsync("retro-2");
        reloaded!.RetroBoard!.Cards.Should().ContainSingle()
            .Which.Text.Should().Be("Deploys got faster");
    }

    [Fact]
    public async Task Removing_a_retro_room_cascades_to_its_board_columns_and_cards()
    {
        var room = NewRetroRoom("retro-3");
        await new EfRoomStore(NewContext()).AddAsync(room);

        var adding = new EfRoomStore(NewContext());
        var loaded = await adding.FindByShortCodeAsync("retro-3");
        loaded!.RetroBoard!.Cards.Add(new RetroCard
        {
            Id = Guid.NewGuid(),
            BoardId = loaded.Id,
            ColumnId = loaded.RetroBoard.Columns.First().Id,
            AuthorUserId = "u1",
            Text = "Going away",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await adding.UpdateAsync(loaded);

        var removing = new EfRoomStore(NewContext());
        await removing.RemoveAsync((await removing.FindByShortCodeAsync("retro-3"))!);

        using var ctx = NewContext();
        ctx.Rooms.Any(r => r.ShortCode == "retro-3").Should().BeFalse();
        ctx.Set<RetroCard>().Any().Should().BeFalse();
        ctx.Set<RetroColumn>().Any().Should().BeFalse();
        ctx.Set<RetroBoard>().Any().Should().BeFalse();
    }

    public void Dispose() => _connection.Dispose();
}
