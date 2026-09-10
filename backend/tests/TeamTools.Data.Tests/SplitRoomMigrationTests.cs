using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TeamTools.Core.Models;
using TeamTools.Data.Sqlite;
using Xunit;

namespace TeamTools.Data.Tests;

/// <summary>
/// The #19 refactor split <c>Sessions</c> into <c>Rooms</c> + <c>PokerRounds</c>. EF's scaffolded
/// migration would have dropped the old table and created the new ones empty, silently destroying
/// every existing session. These tests upgrade a database that was populated **under the old
/// schema** and assert the data is still there afterwards — the guarantee ARCHITECTURE.md asks for,
/// and the only thing standing between a real deployment and losing its history.
/// </summary>
public sealed class SplitRoomMigrationTests : IDisposable
{
    /// <summary>The last migration before the split — the state an existing deployment is in.</summary>
    private const string PreSplitMigration = "20260625152827_AddRoundResults";

    private readonly SqliteConnection _connection;
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _roundResultId = Guid.NewGuid();

    public SplitRoomMigrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    private TeamToolsDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TeamToolsDbContext>()
            .UseSqlite(_connection, o => o.MigrationsAssembly(typeof(SqliteDatabaseProvider).Assembly.GetName().Name))
            .Options;
        return new TeamToolsDbContext(options);
    }

    /// <summary>Migrates to the pre-split schema and inserts a session, two seats and a round result.</summary>
    private void SeedPreSplitDatabase()
    {
        using var ctx = NewContext();
        ctx.GetService<IMigrator>().Migrate(PreSplitMigration);

        ctx.Database.ExecuteSqlRaw(
            """
            INSERT INTO "Sessions" (
                "Id", "ShortCode", "Name", "DeckType", "CustomCards", "State", "OrganiserUserId",
                "AutoReveal", "PasswordHash", "CurrentStory", "CurrentStoryNote", "CreatedAt",
                "LastActivityAt", "LinkedProvider", "TicketQueue", "ReactionsEnabled",
                "AllowRoleChange", "TimerDurationSeconds", "TimerDeadline",
                "TimerPausedRemainingSeconds", "ClosedAt", "DeletedAt")
            VALUES (
                {0}, 'blue-fox-42', 'Sprint 12', 'Fibonacci', NULL, 'Revealed', 'alice',
                1, 'pbkdf2$hash', 'PROJ-7 login page', 'Bigger than it looks', '2026-01-01T00:00:00+00:00',
                '2026-01-02T00:00:00+00:00', NULL, '[]', 1,
                1, 300, NULL,
                NULL, NULL, NULL);
            """,
            _sessionId);

        ctx.Database.ExecuteSqlRaw(
            """
            INSERT INTO "Participants" (
                "SessionId", "UserId", "DisplayName", "NormalizedName", "IsOrganiser", "Role",
                "Vote", "HasVoted", "ChangedAfterReveal", "IsConnected", "LastSeenAt")
            VALUES
                ({0}, 'alice', 'Alice', 'alice', 1, 'Observer', NULL, 0, 0, 1, '2026-01-02T00:00:00+00:00'),
                ({0}, 'bob', 'Bob', 'bob', 0, 'Voter', '5', 1, 0, 1, '2026-01-02T00:00:00+00:00');
            """,
            _sessionId);

        ctx.Database.ExecuteSqlRaw(
            """
            INSERT INTO "RoundResult" (
                "Id", "SessionId", "Story", "Note", "FinalEstimate", "Average", "Consensus",
                "VoteCount", "RecordedAt")
            VALUES ({0}, {1}, 'PROJ-6 signup', 'Went fine', '3', 3.0, 1, 2, '2026-01-01T12:00:00+00:00');
            """,
            _roundResultId, _sessionId);
    }

    [Fact]
    public void Upgrading_a_pre_split_database_keeps_the_room_and_its_participants()
    {
        SeedPreSplitDatabase();

        using (var upgrading = NewContext())
        {
            upgrading.Database.Migrate();
        }

        using var ctx = NewContext();
        var room = ctx.Rooms
            .Include(r => r.Participants)
            .Single(r => r.ShortCode == "blue-fox-42");

        room.Id.Should().Be(_sessionId, "the room keeps its identity, so invite links and ids still resolve");
        room.Name.Should().Be("Sprint 12");
        room.OrganiserUserId.Should().Be("alice");
        room.PasswordHash.Should().Be("pbkdf2$hash", "an existing password must still let people in");
        room.ReactionsEnabled.Should().BeTrue();
        room.AllowRoleChange.Should().BeTrue();
        room.CreatedAt.Should().Be(DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
        room.LastActivityAt.Should().Be(DateTimeOffset.Parse("2026-01-02T00:00:00+00:00"));

        // Every pre-existing room was a poker session — the only tool that existed.
        room.Tool.Should().Be(RoomTool.Poker);

        room.Participants.Should().HaveCount(2);
        var bob = room.Participants.Single(p => p.UserId == "bob");
        bob.Vote.Should().Be("5", "a seat mid-round keeps its cast vote across the upgrade");
        bob.HasVoted.Should().BeTrue();
        bob.Role.Should().Be(ParticipantRole.Voter);
        room.Participants.Single(p => p.UserId == "alice").IsOrganiser.Should().BeTrue();
    }

    [Fact]
    public void Upgrading_moves_the_estimation_state_onto_the_poker_round()
    {
        SeedPreSplitDatabase();

        using (var upgrading = NewContext())
        {
            upgrading.Database.Migrate();
        }

        using var ctx = NewContext();
        var round = ctx.Rooms
            .Include(r => r.PokerRound)
            .Single(r => r.ShortCode == "blue-fox-42")
            .PokerRound;

        round.Should().NotBeNull();
        round!.RoomId.Should().Be(_sessionId);
        round.DeckType.Should().Be(DeckType.Fibonacci);
        round.State.Should().Be(SessionState.Revealed, "a room mid-reveal stays mid-reveal");
        round.AutoReveal.Should().BeTrue();
        round.CurrentStory.Should().Be("PROJ-7 login page");
        round.CurrentStoryNote.Should().Be("Bigger than it looks", "story notes (#10) are user-authored content");
        round.TimerDurationSeconds.Should().Be(300);
    }

    [Fact]
    public void Upgrading_keeps_the_completed_round_history()
    {
        SeedPreSplitDatabase();

        using (var upgrading = NewContext())
        {
            upgrading.Database.Migrate();
        }

        using var ctx = NewContext();
        var history = ctx.Rooms
            .Include(r => r.PokerRound)
                .ThenInclude(p => p!.RoundResults)
            .Single(r => r.ShortCode == "blue-fox-42")
            .PokerRound!
            .RoundResults;

        // This is the analytics + export history (#11, #12) — the data with the longest memory.
        history.Should().ContainSingle();
        var result = history[0];
        result.Id.Should().Be(_roundResultId);
        result.RoomId.Should().Be(_sessionId);
        result.Story.Should().Be("PROJ-6 signup");
        result.Note.Should().Be("Went fine");
        result.FinalEstimate.Should().Be("3");
        result.Average.Should().Be(3.0);
        result.Consensus.Should().BeTrue();
        result.VoteCount.Should().Be(2);
    }

    [Fact]
    public void Downgrading_puts_the_two_halves_back_into_one_session_row()
    {
        SeedPreSplitDatabase();

        using (var upgrading = NewContext())
        {
            upgrading.Database.Migrate();
        }

        // A rollback has to be lossless too, or the migration is a one-way door.
        using (var reverting = NewContext())
        {
            reverting.GetService<IMigrator>().Migrate(PreSplitMigration);
        }

        using var ctx = NewContext();
        var name = ctx.Database
            .SqlQueryRaw<string>(@"SELECT ""Name"" AS ""Value"" FROM ""Sessions"" WHERE ""ShortCode"" = 'blue-fox-42'")
            .Single();
        var story = ctx.Database
            .SqlQueryRaw<string>(@"SELECT ""CurrentStory"" AS ""Value"" FROM ""Sessions"" WHERE ""ShortCode"" = 'blue-fox-42'")
            .Single();
        var seats = ctx.Database
            .SqlQueryRaw<int>(@"SELECT COUNT(*) AS ""Value"" FROM ""Participants""")
            .Single();

        name.Should().Be("Sprint 12");
        story.Should().Be("PROJ-7 login page", "the round's half of the row is merged back in");
        seats.Should().Be(2);
    }

    public void Dispose() => _connection.Dispose();
}
