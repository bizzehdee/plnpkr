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

            // The tool payload: one-to-one, sharing the room's primary key, cascading with the room.
            e.HasOne(r => r.PokerRound)
                .WithOne(p => p.Room!)
                .HasForeignKey<PokerRound>(p => p.RoomId)
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
