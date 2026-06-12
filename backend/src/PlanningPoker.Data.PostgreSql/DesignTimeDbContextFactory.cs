using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlanningPoker.Data.PostgreSql;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can scaffold/apply PostgreSQL migrations into this assembly
/// without the API project. The connection string is a placeholder — scaffolding generates SQL from the
/// model and never connects. See #19.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PlanningPokerDbContext>
{
    public PlanningPokerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlanningPokerDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=planningpoker_design;Username=design;Password=design",
                o => o.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new PlanningPokerDbContext(options);
    }
}
