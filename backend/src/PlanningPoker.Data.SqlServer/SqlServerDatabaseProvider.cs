using Microsoft.EntityFrameworkCore;

namespace PlanningPoker.Data.SqlServer;

/// <summary>SQL Server engine (#19); migrations live in this assembly.</summary>
public sealed class SqlServerDatabaseProvider : IDatabaseProvider
{
    public DatabaseProvider Provider => DatabaseProvider.SqlServer;

    public void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, o => o.MigrationsAssembly(typeof(SqlServerDatabaseProvider).Assembly.GetName().Name));
}
