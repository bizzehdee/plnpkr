using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PlanningPoker.Data;
using PlanningPoker.Data.PostgreSql;
using PlanningPoker.Data.Sqlite;
using PlanningPoker.Data.SqlServer;
using Xunit;

namespace PlanningPoker.Data.Tests;

/// <summary>Provider selection for the configurable database engine (#19).</summary>
public class DatabaseConfigurationTests
{
    private static readonly IDatabaseProvider[] AllProviders =
    [
        new SqliteDatabaseProvider(),
        new SqlServerDatabaseProvider(),
        new PostgreSqlDatabaseProvider(),
    ];

    [Theory]
    [InlineData("Sqlite", DatabaseProvider.Sqlite)]
    [InlineData("sqlite", DatabaseProvider.Sqlite)]
    [InlineData("SqlServer", DatabaseProvider.SqlServer)]
    [InlineData("sqlserver", DatabaseProvider.SqlServer)]
    [InlineData("PostgreSql", DatabaseProvider.PostgreSql)]
    [InlineData("MySql", DatabaseProvider.MySql)]
    public void Parses_known_provider_names_case_insensitively(string value, DatabaseProvider expected) =>
        DatabaseConfiguration.ParseProvider(value).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("oracle")] // unknown
    public void Defaults_to_sqlite_for_unknown_or_missing(string? value) =>
        DatabaseConfiguration.ParseProvider(value).Should().Be(DatabaseProvider.Sqlite);

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Select_returns_the_matching_provider(DatabaseProvider provider) =>
        DatabaseConfiguration.Select(provider, AllProviders).Provider.Should().Be(provider);

    [Fact]
    public void Selecting_mysql_fails_clearly_because_it_is_not_yet_enabled()
    {
        var act = () => DatabaseConfiguration.Select(DatabaseProvider.MySql, AllProviders);
        act.Should().Throw<NotSupportedException>().WithMessage("*MySQL*");
    }

    [Fact]
    public void Selecting_a_provider_absent_from_this_build_fails_clearly()
    {
        // A build that only ships SQLite shouldn't be able to select SQL Server.
        var sqliteOnly = new IDatabaseProvider[] { new SqliteDatabaseProvider() };

        var act = () => DatabaseConfiguration.Select(DatabaseProvider.SqlServer, sqliteOnly);
        act.Should().Throw<NotSupportedException>().WithMessage("*not available in this build*");
    }

    [Fact]
    public void Sqlite_provider_configures_a_sqlite_context()
    {
        var options = new DbContextOptionsBuilder<PlanningPokerDbContext>();
        new SqliteDatabaseProvider().Configure(options, "Data Source=:memory:");
        using var db = new PlanningPokerDbContext(options.Options);

        db.Database.IsSqlite().Should().BeTrue();
    }

    [Fact]
    public void SqlServer_provider_configures_a_sqlserver_context()
    {
        var options = new DbContextOptionsBuilder<PlanningPokerDbContext>();
        new SqlServerDatabaseProvider().Configure(options, "Server=x;Database=y;");
        using var db = new PlanningPokerDbContext(options.Options);

        db.Database.IsSqlServer().Should().BeTrue();
    }

    [Fact]
    public void PostgreSql_provider_configures_an_npgsql_context()
    {
        var options = new DbContextOptionsBuilder<PlanningPokerDbContext>();
        new PostgreSqlDatabaseProvider().Configure(options, "Host=x;Database=y;Username=u;Password=p");
        using var db = new PlanningPokerDbContext(options.Options);

        db.Database.IsNpgsql().Should().BeTrue();
    }
}
