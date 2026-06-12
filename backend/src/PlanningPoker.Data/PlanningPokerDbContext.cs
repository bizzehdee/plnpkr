using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PlanningPoker.Core.Models;

namespace PlanningPoker.Data;

public class PlanningPokerDbContext : DbContext
{
    public PlanningPokerDbContext(DbContextOptions<PlanningPokerDbContext> options)
        : base(options)
    {
    }

    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Participant> Participants => Set<Participant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Session>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.ShortCode).IsRequired().HasMaxLength(64);
            e.HasIndex(s => s.ShortCode).IsUnique();
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.Property(s => s.DeckType).HasConversion<string>();
            e.Property(s => s.State).HasConversion<string>();
            e.Property(s => s.CustomCards).HasMaxLength(1000);
            e.Property(s => s.CurrentStory).HasMaxLength(500);
            // Optional join password, stored as an encoded KDF hash (never plaintext). See #2.
            e.Property(s => s.PasswordHash).HasMaxLength(256);

            // Issue-tracker integration (#4). Provider as string; linked ticket as an optional
            // owned entity (its columns are nullable, so a session with no linked issue stores nulls).
            e.Property(s => s.LinkedProvider).HasConversion<string>().HasMaxLength(32);
            e.OwnsOne(s => s.LinkedIssue, li =>
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
            e.Property(s => s.TicketQueue)
                .HasConversion(queueConverter, queueComparer)
                .HasColumnName("TicketQueue");

            e.HasMany(s => s.Participants)
                .WithOne(p => p.Session!)
                .HasForeignKey(p => p.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Soft delete (#26): a deleted session is hidden from every query.
            e.HasQueryFilter(s => s.DeletedAt == null);
        });

        modelBuilder.Entity<Participant>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.UserId).IsRequired().HasMaxLength(64);
            e.Property(p => p.DisplayName).IsRequired().HasMaxLength(80);
            e.Property(p => p.NormalizedName).IsRequired().HasMaxLength(80);
            e.Property(p => p.Role).HasConversion<string>();
            e.Property(p => p.Vote).HasMaxLength(16);

            // Per-session uniqueness for display names (case-insensitive via NormalizedName). #7.
            e.HasIndex(p => new { p.SessionId, p.NormalizedName }).IsUnique();
            // A given browser identity appears at most once per session. #34.
            e.HasIndex(p => new { p.SessionId, p.UserId }).IsUnique();
        });
    }
}
