namespace PlanningPoker.Core.Integrations;

/// <summary>
/// Sanitises a ticket description's HTML before it enters a snapshot/broadcast — ticket content is
/// third-party, so scripts, event handlers, iframes and the like must be removed while safe
/// formatting (lists, links, bold, code, paragraphs…) is kept. See #35.
/// </summary>
public interface IHtmlDescriptionSanitizer
{
    /// <summary>Returns sanitised HTML, or null/empty unchanged.</summary>
    string? Sanitize(string? html);
}
