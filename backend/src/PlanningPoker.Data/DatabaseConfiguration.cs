namespace PlanningPoker.Data;

/// <summary>
/// Selects the configured database engine from the set of providers a build makes available (#19).
/// SQLite stays the default so there's nothing to set up locally; each engine's driver + migrations live
/// in its own assembly (<c>PlanningPoker.Data.Sqlite</c> / <c>.SqlServer</c> / <c>.PostgreSql</c>).
/// </summary>
public static class DatabaseConfiguration
{
    /// <summary>Parses the configured provider name; unknown/empty falls back to <see cref="DatabaseProvider.Sqlite"/>.</summary>
    public static DatabaseProvider ParseProvider(string? value) =>
        Enum.TryParse<DatabaseProvider>(value, ignoreCase: true, out var provider) ? provider : DatabaseProvider.Sqlite;

    /// <summary>
    /// Picks the implementation for <paramref name="provider"/> from those <paramref name="available"/> in
    /// this build. Throws a clear error if it isn't available — e.g. MySQL (not yet enabled, no stable EF
    /// Core 10 provider) or an engine whose project simply isn't referenced by this deployment.
    /// </summary>
    public static IDatabaseProvider Select(DatabaseProvider provider, IEnumerable<IDatabaseProvider> available)
    {
        var match = available.FirstOrDefault(p => p.Provider == provider);
        if (match is not null)
        {
            return match;
        }

        var reason = provider == DatabaseProvider.MySql
            ? "MySQL/MariaDB isn't enabled yet: no stable EF Core 10 provider is available."
            : $"Database provider '{provider}' is not available in this build.";
        throw new NotSupportedException($"{reason} See #19.");
    }
}
