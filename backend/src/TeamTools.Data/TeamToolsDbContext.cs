using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TeamTools.Core.Models;

namespace TeamTools.Data;

public class TeamToolsDbContext : DbContext
{
    public TeamToolsDbContext(DbContextOptions<TeamToolsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<PokerRound> PokerRounds => Set<PokerRound>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // --- Room: the tool-agnostic engine (#19) ---------------------------
        modelBuilder.Entity<Room>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.ShortCode).IsRequired().HasMaxLength(64);
            e.HasIndex(r => r.ShortCode).IsUnique();
            e.Property(r => r.Name).IsRequired().HasMaxLength(200);
            // Which tool the room hosts; stored as a string so the column reads plainly in the DB.
            e.Property(r => r.Tool).HasConversion<string>().HasMaxLength(16);
            // Optional join password, stored as an encoded KDF hash (never plaintext). See #2.
            e.Property(r => r.PasswordHash).HasMaxLength(256);
            e.Ignore(r => r.IsClosed); // computed from ClosedAt

            e.HasMany(r => r.Participants)
                .WithOne(p => p.Room!)
                .HasForeignKey(p => p.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            // The tool payloads: one-to-one, sharing the room's primary key, cascading with the room.
            // Exactly one is non-null, per Tool.
            e.HasOne(r => r.PokerRound)
                .WithOne(p => p.Room!)
                .HasForeignKey<PokerRound>(p => p.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.RetroBoard)
                .WithOne(b => b.Room!)
                .HasForeignKey<RetroBoard>(b => b.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            // Soft delete (#26): a deleted room is hidden from every query.
            e.HasQueryFilter(r => r.DeletedAt == null);
        });

        // --- PokerRound: the estimation payload (#19) -----------------------
        modelBuilder.Entity<PokerRound>(e =>
        {
            e.HasKey(p => p.RoomId);
            e.Property(p => p.DeckType).HasConversion<string>();
            e.Property(p => p.State).HasConversion<string>();
            e.Property(p => p.CustomCards).HasMaxLength(1000);
            e.Property(p => p.CurrentStory).HasMaxLength(500);
            e.Property(p => p.CurrentStoryNote).HasMaxLength(2000); // story note (#10)
            e.Ignore(p => p.IsRevealed); // computed from State

            // Issue-tracker integration (#4). Provider as string; linked ticket as an optional
            // owned entity (its columns are nullable, so a round with no linked issue stores nulls).
            e.Property(p => p.LinkedProvider).HasConversion<string>().HasMaxLength(32);
            e.OwnsOne(p => p.LinkedIssue, li =>
            {
                li.Property(x => x.Key).HasMaxLength(64);
                li.Property(x => x.Title).HasMaxLength(500);
                li.Property(x => x.Description).HasMaxLength(8000);
                li.Property(x => x.Url).HasMaxLength(1000);
            });

            // Ticket queue (#38) persisted as a single JSON column (provider-agnostic on SQLite).
            var jsonOptions = new JsonSerializerOptions();
            var queueConverter = new ValueConverter<List<QueuedTicket>, string>(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => string.IsNullOrEmpty(v) ? new List<QueuedTicket>() : JsonSerializer.Deserialize<List<QueuedTicket>>(v, jsonOptions)!);
            var queueComparer = new ValueComparer<List<QueuedTicket>>(
                (a, b) => JsonSerializer.Serialize(a, jsonOptions) == JsonSerializer.Serialize(b, jsonOptions),
                v => JsonSerializer.Serialize(v, jsonOptions).GetHashCode(),
                v => JsonSerializer.Deserialize<List<QueuedTicket>>(JsonSerializer.Serialize(v, jsonOptions), jsonOptions)!);
            e.Property(p => p.TicketQueue)
                .HasConversion(queueConverter, queueComparer)
                .HasColumnName("TicketQueue");

            // Completed-round history for analytics/export (#11). Own table, cascade with the round.
            e.HasMany(p => p.RoundResults)
                .WithOne(r => r.Round!)
                .HasForeignKey(r => r.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // --- RetroBoard: the retrospective payload (#21) --------------------
        modelBuilder.Entity<RetroBoard>(e =>
        {
            e.HasKey(b => b.RoomId);
            e.Property(b => b.Template).HasConversion<string>().HasMaxLength(32);
            e.Property(b => b.Phase).HasConversion<string>().HasMaxLength(16);

            e.HasMany(b => b.Columns)
                .WithOne(c => c.Board!)
                .HasForeignKey(c => c.BoardId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(b => b.Cards)
                .WithOne(c => c.Board!)
                .HasForeignKey(c => c.BoardId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(b => b.Groups)
                .WithOne(g => g.Board!)
                .HasForeignKey(g => g.BoardId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(b => b.Votes)
                .WithOne(v => v.Board!)
                .HasForeignKey(v => v.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // --- RetroVote: one row per dot (#25) -------------------------------
        modelBuilder.Entity<RetroVote>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Id).ValueGeneratedNever(); // the domain assigns it — see RetroCard.Id
            e.Property(v => v.VoterUserId).IsRequired().HasMaxLength(64);
            e.Property(v => v.TargetKind).HasConversion<string>().HasMaxLength(8);
            e.HasIndex(v => v.BoardId);
            // The budget check counts this voter.s rows, and the tallies count an item.s.
            e.HasIndex(v => new { v.BoardId, v.VoterUserId });
            e.HasIndex(v => new { v.TargetKind, v.TargetId });
        });

        // --- RetroGroup: a theme (#24) --------------------------------------
        modelBuilder.Entity<RetroGroup>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.Id).ValueGeneratedNever(); // the domain assigns it — see RetroCard.Id
            e.Property(g => g.Label).IsRequired().HasMaxLength(120);
            e.HasIndex(g => g.BoardId);

            // SetNull, not Cascade: deleting a theme must not delete the team.s cards with it, and a
            // second cascade path to the same rows is what SQL Server rejects as a cycle.
            e.HasMany(g => g.Cards)
                .WithOne(c => c.Group!)
                .HasForeignKey(c => c.GroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RetroColumn>(e =>
        {
            e.HasKey(c => c.Id);
            // The domain assigns these ids (RetroService), so EF must not treat a set key as proof
            // the row already exists — see the note on RetroCard.Id below.
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Title).IsRequired().HasMaxLength(60);
            e.HasIndex(c => c.BoardId);

            // A card's column is set by the client, so the relationship is explicit. NoAction (not
            // Cascade): the card already cascades from the board, and a second cascade path to the
            // same rows is what SQL Server rejects as a multiple-cascade-path cycle.
            e.HasMany(c => c.Cards)
                .WithOne(card => card.Column!)
                .HasForeignKey(card => card.ColumnId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RetroCard>(e =>
        {
            e.HasKey(c => c.Id);
            // `RetroService` assigns card ids itself (it is pure and testable against an in-memory
            // store, which generates nothing). Without ValueGeneratedNever, EF sees a non-default
            // Guid key on an entity added to an *already-tracked* room and infers the row exists,
            // issuing an UPDATE that matches nothing — a DbUpdateConcurrencyException instead of an
            // insert. Creation happened to work because a whole new graph is Added wholesale.
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.AuthorUserId).IsRequired().HasMaxLength(64);
            // Matches RetroService.MaxCardLength — the service rejects longer text before it gets here.
            e.Property(c => c.Text).IsRequired().HasMaxLength(500);
            e.HasIndex(c => c.BoardId);
            e.HasIndex(c => c.ColumnId);
            e.HasIndex(c => c.GroupId);
        });

        modelBuilder.Entity<RoundResult>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Story).HasMaxLength(500);
            e.Property(r => r.Note).HasMaxLength(2000);
            e.Property(r => r.FinalEstimate).HasMaxLength(16);
            e.HasIndex(r => r.RoomId);
        });

        modelBuilder.Entity<Participant>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.UserId).IsRequired().HasMaxLength(64);
            e.Property(p => p.DisplayName).IsRequired().HasMaxLength(80);
            e.Property(p => p.NormalizedName).IsRequired().HasMaxLength(80);
            e.Property(p => p.Role).HasConversion<string>();
            e.Property(p => p.Vote).HasMaxLength(16);

            // Per-room uniqueness for display names (case-insensitive via NormalizedName). #7.
            e.HasIndex(p => new { p.RoomId, p.NormalizedName }).IsUnique();
            // A given browser identity appears at most once per room. #34.
            e.HasIndex(p => new { p.RoomId, p.UserId }).IsUnique();
        });
    }
}
