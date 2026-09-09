using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TeamTools.Data.Sqlite;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can scaffold/apply SQLite migrations into this assembly
/// without the API project. The connection string is a placeholder. See #19.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TeamToolsDbContext>
{
    public TeamToolsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TeamToolsDbContext>()
            .UseSqlite(
                "Data Source=teamtools.db",
                o => o.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new TeamToolsDbContext(options);
    }
}
