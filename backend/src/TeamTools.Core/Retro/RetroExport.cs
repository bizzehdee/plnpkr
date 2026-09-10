using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeamTools.Core.Models;

namespace TeamTools.Core.Retro;

/// <summary>Why an export was refused, or Ok. See #28.</summary>
public enum RetroExportStatus
{
    Ok,
    BoardNotFound,
    /// <summary>The board has a password and the wrong one (or none) was supplied.</summary>
    PasswordRequired,
    /// <summary>
    /// The retro has not reached the discussion yet, so exporting it would hand out cards the team
    /// has not seen and dot totals it has not reached.
    /// </summary>
    NotYetVisible,
}

/// <summary>
/// A retro board rendered for export (#28): the columns and cards, the themes with their dot
/// tallies, and the action items.
/// <para>
/// <b>Authorship obeys the board's anonymity setting</b> (#22): on an anonymous board no author
/// appears in any format, and there is no organiser override. An export that quietly attributed
/// anonymous cards would be the worst possible bug in this feature.
/// </para>
/// </summary>
public record RetroExport(
    string ShortCode,
    string Name,
    RetroTemplate Template,
    RetroPhase Phase,
    bool Anonymous,
    string? CarriedFromShortCode,
    IReadOnlyList<RetroExportTheme> Themes,
    IReadOnlyList<RetroExportCard> LooseCards,
    IReadOnlyList<RetroExportAction> Actions);

public record RetroExportTheme(string Label, int Dots, IReadOnlyList<RetroExportCard> Cards);

public record RetroExportCard(string Column, string Text, string? Author, int Dots);

public record RetroExportAction(
    string Title, string? Owner, DateTimeOffset? DueDate, bool Done, bool CarriedOver);

/// <summary>
/// Renders a <see cref="RetroExport"/> to the formats teams actually paste into things (#28):
/// Markdown for a wiki or a ticket, CSV for a spreadsheet, JSON for anything else. Pure, so the
/// shapes — and the anonymity guarantee — are testable without a store or a web host.
/// </summary>
public static class RetroExportRenderer
{
    /// <summary>
    /// The export as JSON, in the platform's own wire casing.
    /// <para>
    /// <b>camelCase with named enums, not the serializer's defaults.</b> This payload is not only a
    /// file — the read-only summary view reads it directly (#28) — so it has to look like every
    /// other TeamTools payload the SPA parses. A machine consumer does not care either way; a
    /// second casing convention inside one app would.
    /// </para>
    /// </summary>
    public static string ToJson(RetroExport export) => JsonSerializer.Serialize(export, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Markdown grouped by theme and ordered by dots, with the actions as a task list — the format
    /// that gets pasted into a wiki page the morning after.
    /// </summary>
    public static string ToMarkdown(RetroExport export)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(export.Name);
        sb.AppendLine();
        if (export.Anonymous)
        {
            sb.AppendLine("_Cards in this retro were anonymous._");
            sb.AppendLine();
        }
        if (export.CarriedFromShortCode is { } from)
        {
            sb.Append("_Actions carried forward from `").Append(from).AppendLine("`._");
            sb.AppendLine();
        }

        if (export.Themes.Count > 0)
        {
            sb.AppendLine("## Themes");
            sb.AppendLine();
            foreach (var theme in export.Themes)
            {
                sb.Append("### ").Append(theme.Label)
                  .Append(" — ").Append(Dots(theme.Dots)).AppendLine();
                sb.AppendLine();
                foreach (var card in theme.Cards)
                {
                    AppendCard(sb, card, export.Anonymous);
                }
                sb.AppendLine();
            }
        }

        if (export.LooseCards.Count > 0)
        {
            sb.AppendLine("## Cards");
            sb.AppendLine();
            foreach (var card in export.LooseCards)
            {
                AppendCard(sb, card, export.Anonymous);
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Action items");
        sb.AppendLine();
        if (export.Actions.Count == 0)
        {
            sb.AppendLine("_None._");
        }
        else
        {
            foreach (var action in export.Actions)
            {
                sb.Append("- [").Append(action.Done ? 'x' : ' ').Append("] ").Append(action.Title);
                if (action.Owner is { } owner)
                {
                    sb.Append(" — ").Append(owner);
                }
                if (action.DueDate is { } due)
                {
                    sb.Append(" (due ").Append(due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(')');
                }
                if (action.CarriedOver)
                {
                    sb.Append(" _(carried over)_");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();

        static void AppendCard(StringBuilder sb, RetroExportCard card, bool anonymous)
        {
            sb.Append("- **").Append(card.Column).Append("** — ").Append(card.Text);
            if (!anonymous && card.Author is { } author)
            {
                sb.Append(" _(").Append(author).Append(")_");
            }
            if (card.Dots > 0)
            {
                sb.Append(" — ").Append(Dots(card.Dots));
            }
            sb.AppendLine();
        }

        static string Dots(int count) => count == 1 ? "1 dot" : $"{count} dots";
    }

    /// <summary>
    /// One row per card and one per action, so a spreadsheet can pivot either. The Author column is
    /// **absent entirely** on an anonymous board rather than blank — a blank column invites someone
    /// to ask where the data went.
    /// </summary>
    public static string ToCsv(RetroExport export)
    {
        var sb = new StringBuilder();
        var withAuthor = !export.Anonymous;

        sb.AppendLine(withAuthor
            ? "Kind,Theme,Column,Text,Author,Dots,Owner,DueDate,Done,CarriedOver"
            : "Kind,Theme,Column,Text,Dots,Owner,DueDate,Done,CarriedOver");

        foreach (var theme in export.Themes)
        {
            foreach (var card in theme.Cards)
            {
                AppendCardRow(sb, "card", theme.Label, card, withAuthor);
            }
        }

        foreach (var card in export.LooseCards)
        {
            AppendCardRow(sb, "card", string.Empty, card, withAuthor);
        }

        foreach (var action in export.Actions)
        {
            var cells = new List<string> { "action", string.Empty, string.Empty, Csv.Field(action.Title) };
            if (withAuthor)
            {
                cells.Add(string.Empty);
            }
            cells.Add(string.Empty); // Dots
            cells.Add(Csv.Field(action.Owner));
            cells.Add(Csv.Date(action.DueDate));
            cells.Add(action.Done ? "true" : "false");
            cells.Add(action.CarriedOver ? "true" : "false");
            sb.AppendLine(string.Join(',', cells));
        }

        return sb.ToString();

        static void AppendCardRow(
            StringBuilder sb, string kind, string theme, RetroExportCard card, bool withAuthor)
        {
            var cells = new List<string> { kind, Csv.Field(theme), Csv.Field(card.Column), Csv.Field(card.Text) };
            if (withAuthor)
            {
                cells.Add(Csv.Field(card.Author));
            }
            cells.Add(card.Dots.ToString(CultureInfo.InvariantCulture));
            cells.Add(string.Empty); // Owner
            cells.Add(string.Empty); // DueDate
            cells.Add(string.Empty); // Done
            cells.Add(string.Empty); // CarriedOver
            sb.AppendLine(string.Join(',', cells));
        }
    }
}
