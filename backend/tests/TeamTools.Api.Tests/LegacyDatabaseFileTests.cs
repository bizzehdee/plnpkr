using FluentAssertions;
using Xunit;

namespace TeamTools.Api.Tests;

/// <summary>
/// The default SQLite filename changed with the plnpkr → TeamTools rename (#18). An existing
/// deployment on the default must keep its data rather than silently starting empty.
/// </summary>
public class LegacyDatabaseFileTests
{
    private static Func<string, bool> Present(params string[] files) => name => files.Contains(name);

    [Fact]
    public void Uses_the_new_default_on_a_fresh_install()
    {
        var connectionString = LegacyDatabaseFile.ResolveDefaultConnectionString(Present());

        connectionString.Should().Be("Data Source=teamtools.db");
        LegacyDatabaseFile.LegacyHint(connectionString).Should().BeNull();
    }

    [Fact]
    public void Keeps_using_the_legacy_file_when_it_is_the_only_one_present()
    {
        var connectionString = LegacyDatabaseFile.ResolveDefaultConnectionString(Present("planningpoker.db"));

        connectionString.Should().Be("Data Source=planningpoker.db");
    }

    [Fact]
    public void Warns_when_the_legacy_file_is_in_use()
    {
        var connectionString = LegacyDatabaseFile.ResolveDefaultConnectionString(Present("planningpoker.db"));

        LegacyDatabaseFile.LegacyHint(connectionString)
            .Should().Contain("planningpoker.db").And.Contain("teamtools.db");
    }

    [Fact]
    public void Prefers_the_new_file_when_both_exist()
    {
        var connectionString = LegacyDatabaseFile.ResolveDefaultConnectionString(
            Present("teamtools.db", "planningpoker.db"));

        connectionString.Should().Be("Data Source=teamtools.db");
        LegacyDatabaseFile.LegacyHint(connectionString).Should().BeNull();
    }
}
