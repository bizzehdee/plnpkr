using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlanningPoker.Data.Sqlite;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can scaffold/apply SQLite migrations into this assembly
/// without the API project. The connection string is a placeholder. See #19.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PlanningPokerDbContext>
{
    public PlanningPokerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlanningPokerDbContext>()
            .UseSqlite(
                "Data Source=planningpoker.db",
                o => o.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new PlanningPokerDbContext(options);
    }
}
