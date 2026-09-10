using System.Globalization;

namespace TeamTools.Core;

/// <summary>
/// RFC-4180 CSV field escaping, shared by both tools' exports (#34).
/// <para>
/// Extracted because it was character-for-character identical in <c>PokerService</c> (#12) and
/// <c>RetroExportRenderer</c> (#28), and because getting it subtly wrong in one of them is the kind
/// of bug that shows up as a customer's spreadsheet quietly losing a column.
/// </para>
/// </summary>
public static class Csv
{
    /// <summary>
    /// Escapes one field: quoted when it contains a comma, a quote, CR or LF, with inner quotes
    /// doubled. Null and empty both render as empty.
    /// </summary>
    public static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    /// <summary>An invariant-culture date, so an export opens the same way in every locale.</summary>
    public static string Date(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>A joined row of already-escaped fields.</summary>
    public static string Row(params string?[] fields) =>
        string.Join(',', fields.Select(Field));
}
