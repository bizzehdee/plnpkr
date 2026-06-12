using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PlanningPoker.Data.Sqlite;

/// <summary>
/// On-disk SQLite engine (#19, the default). Owns the SQLite-specific concerns: ensuring the
/// database directory exists (SQLite won't create it, and App Service persistent paths like /home/data
/// don't exist yet), and enabling WAL after migration for better read/write concurrency under SignalR.
/// </summary>
public sealed class SqliteDatabaseProvider : IDatabaseProvider
{
    public DatabaseProvider Provider => DatabaseProvider.Sqlite;

    public void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        EnsureDataDirectory(connectionString);
        options.UseSqlite(connectionString, o => o.MigrationsAssembly(typeof(SqliteDatabaseProvider).Assembly.GetName().Name));
    }

    public void OnMigrated(DbContext db) =>
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    private static void EnsureDataDirectory(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
