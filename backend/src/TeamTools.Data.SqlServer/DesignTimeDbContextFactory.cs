using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TeamTools.Data.SqlServer;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can scaffold/apply SQL Server migrations into this assembly
/// without the API project. The connection string is a placeholder — scaffolding generates SQL from the
/// model and never connects. See #19.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TeamToolsDbContext>
{
    public TeamToolsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TeamToolsDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\design;Database=TeamToolsDesign;Trusted_Connection=True;TrustServerCertificate=True",
                o => o.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new TeamToolsDbContext(options);
    }
}
