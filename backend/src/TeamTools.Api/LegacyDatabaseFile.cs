namespace TeamTools.Api;

/// <summary>
/// Resolves the default SQLite connection string across the plnpkr → TeamTools rename (#18).
/// <para>
/// The default database filename changed from <c>planningpoker.db</c> to <c>teamtools.db</c>. An
/// existing deployment that relied on the default would otherwise start against a brand-new, empty
/// database and look like it had lost every session — so if the legacy file is present and the new
/// one is not, we keep using the legacy file and log a hint to rename it. Explicitly configured
/// connection strings are never touched.
/// </para>
/// </summary>
public static class LegacyDatabaseFile
{
    public const string DefaultFileName = "teamtools.db";
    public const string LegacyFileName = "planningpoker.db";

    /// <summary>
    /// The default connection string: the legacy file when it is the only one present, otherwise the
    /// new default. <paramref name="fileExists"/> is injectable so the decision is unit-testable.
    /// </summary>
    public static string ResolveDefaultConnectionString(Func<string, bool>? fileExists = null)
    {
        var exists = fileExists ?? File.Exists;
        var useLegacy = !exists(DefaultFileName) && exists(LegacyFileName);
        return $"Data Source={(useLegacy ? LegacyFileName : DefaultFileName)}";
    }

    /// <summary>
    /// A one-line hint to log at startup when the legacy file is in use, or null when it is not.
    /// Kept separate from <see cref="ResolveDefaultConnectionString"/> because the connection string
    /// is needed before the logging pipeline exists.
    /// </summary>
    public static string? LegacyHint(string connectionString) =>
        connectionString.Contains(LegacyFileName, StringComparison.OrdinalIgnoreCase)
            ? $"Using the legacy database file '{LegacyFileName}'. The default is now " +
              $"'{DefaultFileName}' — rename the file (and its -shm/-wal siblings) or set " +
              "ConnectionStrings__Default explicitly to silence this message."
            : null;
}
