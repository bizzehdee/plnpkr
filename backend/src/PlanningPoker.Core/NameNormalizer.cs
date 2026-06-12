namespace PlanningPoker.Core;

/// <summary>
/// Normalizes display names for the per-session uniqueness check: trimmed and lower-cased so
/// "Alice", "alice " and "ALICE" collide. See #7.
/// </summary>
public static class NameNormalizer
{
    public static string Normalize(string? name) =>
        (name ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsBlank(string? name) => string.IsNullOrWhiteSpace(name);
}
