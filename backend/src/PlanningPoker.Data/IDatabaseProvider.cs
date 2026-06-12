using Microsoft.EntityFrameworkCore;

namespace PlanningPoker.Data;

/// <summary>The relational engine backing the app. Selected by configuration. See #19.</summary>
public enum DatabaseProvider
{
    /// <summary>On-disk SQLite (default; zero-config).</summary>
    Sqlite,
    SqlServer,
    /// <summary>MySQL / MariaDB — recognised but not yet enabled (no stable EF Core 10 provider). See #19.</summary>
    MySql,
    PostgreSql,
}

/// <summary>
/// A pluggable database engine (#19). Each provider lives in its own assembly together with its
/// migrations and its EF Core driver package, so a build only ships the engine(s) it references — e.g.
/// a SQLite-only deployment never drags in the SQL Server or Npgsql drivers. The composition root
/// (the API) picks which providers are available; <see cref="DatabaseConfiguration.Select"/> chooses
/// the configured one at startup.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>Which provider this implementation backs.</summary>
    DatabaseProvider Provider { get; }

    /// <summary>Configures the <see cref="PlanningPokerDbContext"/> for this engine (UseXxx + migrations assembly).</summary>
    void Configure(DbContextOptionsBuilder options, string connectionString);

    /// <summary>Optional one-off step right after <c>Database.Migrate()</c> (e.g. SQLite's WAL pragma). No-op by default.</summary>
    void OnMigrated(DbContext db) { }
}
