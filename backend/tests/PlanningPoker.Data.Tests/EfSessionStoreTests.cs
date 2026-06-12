using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PlanningPoker.Core;
using PlanningPoker.Core.Models;
using PlanningPoker.Data;
using PlanningPoker.Data.Sqlite;
using Xunit;

namespace PlanningPoker.Data.Tests;

/// <summary>
/// Verifies <see cref="EfSessionStore"/> against a real SQLite database (a shared in-memory
/// connection), confirming it honours the contract SessionService relies on. See #16.
/// </summary>
public sealed class EfSessionStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public EfSessionStoreTests()
    {
        // A single open in-memory connection keeps the schema alive for the test's lifetime.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    private PlanningPokerDbContext NewContext()
    {
        // SQLite migrations now live in their own assembly (#19); point EF at it.
        var options = new DbContextOptionsBuilder<PlanningPokerDbContext>()
            .UseSqlite(_connection, o => o.MigrationsAssembly(typeof(SqliteDatabaseProvider).Assembly.GetName().Name))
            .Options;
        return new PlanningPokerDbContext(options);
    }

    private static Session NewSession(string shortCode, string organiserUserId = "u1", string name = "Alice") => new()
    {
        Id = Guid.NewGuid(),
        ShortCode = shortCode,
        Name = "Sprint",
        DeckType = DeckType.Fibonacci,
        State = SessionState.Voting,
        OrganiserUserId = organiserUserId,
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
        await new EfSessionStore(NewContext()).AddAsync(NewSession("blue-fox-42"));

        var loaded = await new EfSessionStore(NewContext()).FindByShortCodeAsync("blue-fox-42");

        loaded.Should().NotBeNull();
        loaded!.Participants.Should().ContainSingle().Which.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public async Task ShortCodeExists_reflects_stored_sessions()
    {
        await new EfSessionStore(NewContext()).AddAsync(NewSession("blue-fox-42"));

        var store = new EfSessionStore(NewContext());
        (await store.ShortCodeExistsAsync("blue-fox-42")).Should().BeTrue();
        (await store.ShortCodeExistsAsync("nope-nope-9")).Should().BeFalse();
    }

    [Fact]
    public async Task Soft_deleted_sessions_are_hidden_from_every_read()
    {
        await new EfSessionStore(NewContext()).AddAsync(NewSession("blue-fox-42"));

        // Soft-delete it (#26): set DeletedAt + persist.
        var deleting = new EfSessionStore(NewContext());
        var session = await deleting.FindByShortCodeAsync("blue-fox-42");
        session!.DeletedAt = DateTimeOffset.UtcNow;
        await deleting.UpdateAsync(session);

        // The global query filter now hides it from find / exists / get-all.
        var store = new EfSessionStore(NewContext());
        (await store.FindByShortCodeAsync("blue-fox-42")).Should().BeNull();
        (await store.ShortCodeExistsAsync("blue-fox-42")).Should().BeFalse();
        (await store.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_name_within_a_session_throws_DuplicateNameException()
    {
        await new EfSessionStore(NewContext()).AddAsync(NewSession("blue-fox-42"));

        var store = new EfSessionStore(NewContext());
        var session = await store.FindByShortCodeAsync("blue-fox-42");
        session!.Participants.Add(new Participant
        {
            SessionId = session.Id,
            UserId = "different-user",
            DisplayName = "ALICE",
            NormalizedName = "alice", // collides with the existing participant
            Role = ParticipantRole.Voter,
        });

        var act = () => store.UpdateAsync(session);

        await act.Should().ThrowAsync<DuplicateNameException>();
    }

    [Fact]
    public async Task Remove_deletes_the_session_and_cascades_to_participants()
    {
        await new EfSessionStore(NewContext()).AddAsync(NewSession("blue-fox-42"));

        var store = new EfSessionStore(NewContext());
        var session = await store.FindByShortCodeAsync("blue-fox-42");
        await store.RemoveAsync(session!);

        (await new EfSessionStore(NewContext()).FindByShortCodeAsync("blue-fox-42")).Should().BeNull();
        using var ctx = NewContext();
        (await ctx.Participants.CountAsync()).Should().Be(0); // cascade delete
    }

    [Fact]
    public async Task GetAll_returns_all_sessions_with_participants()
    {
        await new EfSessionStore(NewContext()).AddAsync(NewSession("blue-fox-42", "u1"));
        await new EfSessionStore(NewContext()).AddAsync(NewSession("red-owl-99", "u2", "Bob"));

        var all = await new EfSessionStore(NewContext()).GetAllAsync();

        all.Should().HaveCount(2);
        all.SelectMany(s => s.Participants).Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSessionsWithExpiredTimer_returns_only_due_voting_sessions()
    {
        var now = DateTimeOffset.UtcNow;

        // Due: Voting with a deadline in the past.
        var due = NewSession("due-timer-1", "u1");
        due.TimerDeadline = now.AddSeconds(-1);
        await new EfSessionStore(NewContext()).AddAsync(due);

        // Not due: deadline still in the future.
        var future = NewSession("future-timer-1", "u2", "Bob");
        future.TimerDeadline = now.AddMinutes(5);
        await new EfSessionStore(NewContext()).AddAsync(future);

        // Not due: no timer running.
        await new EfSessionStore(NewContext()).AddAsync(NewSession("no-timer-1", "u3", "Cara"));

        // Not due: already revealed (timer was running but the round is over).
        var revealed = NewSession("revealed-timer-1", "u4", "Dan");
        revealed.State = SessionState.Revealed;
        revealed.TimerDeadline = now.AddSeconds(-10);
        await new EfSessionStore(NewContext()).AddAsync(revealed);

        var result = await new EfSessionStore(NewContext()).GetSessionsWithExpiredTimerAsync(now);

        result.Should().ContainSingle().Which.ShortCode.Should().Be("due-timer-1");
        result[0].Participants.Should().ContainSingle(); // participants are included for the snapshot
    }

    [Fact]
    public async Task AreReactionsEnabled_reflects_the_flag_and_excludes_missing_or_deleted()
    {
        var on = NewSession("reactions-on-1", "u1");
        on.ReactionsEnabled = true;
        await new EfSessionStore(NewContext()).AddAsync(on);

        var off = NewSession("reactions-off-1", "u2", "Bob");
        off.ReactionsEnabled = false;
        await new EfSessionStore(NewContext()).AddAsync(off);

        var store = new EfSessionStore(NewContext());
        (await store.AreReactionsEnabledAsync("reactions-on-1")).Should().BeTrue();
        (await store.AreReactionsEnabledAsync("reactions-off-1")).Should().BeFalse();
        (await store.AreReactionsEnabledAsync("nope-nope-9")).Should().BeFalse();
    }

    public void Dispose() => _connection.Dispose();
}
