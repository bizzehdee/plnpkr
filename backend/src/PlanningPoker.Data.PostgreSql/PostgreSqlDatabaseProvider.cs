using Microsoft.EntityFrameworkCore;

namespace PlanningPoker.Data.PostgreSql;

/// <summary>PostgreSQL engine (#19); migrations live in this assembly.</summary>
public sealed class PostgreSqlDatabaseProvider : IDatabaseProvider
{
    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    public void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, o => o.MigrationsAssembly(typeof(PostgreSqlDatabaseProvider).Assembly.GetName().Name));
}
