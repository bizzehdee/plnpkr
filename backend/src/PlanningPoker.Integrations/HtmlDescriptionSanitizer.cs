using Ganss.Xss;
using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Integrations;

/// <summary>
/// HTML sanitizer for ticket descriptions, backed by Ganss HtmlSanitizer. Strips scripts, event
/// handlers, iframes, etc., and keeps a safe formatting subset. See #35.
/// </summary>
public sealed class HtmlDescriptionSanitizer : IHtmlDescriptionSanitizer
{
    private readonly HtmlSanitizer _sanitizer;
    private readonly IReadOnlyList<string> _allowedTags =
        [
            "p", "br", "span", "div", "strong", "b", "em", "i", "u", "s", "del", "ins", "sub", "sup",
            "blockquote", "code", "pre", "hr",
            "ul", "ol", "li",
            "h1", "h2", "h3", "h4", "h5", "h6",
            "a", "table", "thead", "tbody", "tr", "th", "td",
            "code", "pre", "tt"
        ];

    public HtmlDescriptionSanitizer()
    {
        _sanitizer = new HtmlSanitizer();
        // Sensible allowlist for issue descriptions: text formatting, lists, links, code, tables.
        _sanitizer.AllowedTags.Clear();
        foreach (var tag in _allowedTags)
        {
            _sanitizer.AllowedTags.Add(tag);
        }

        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedAttributes.Add("href");
        _sanitizer.AllowedAttributes.Add("title");
        _sanitizer.AllowedAttributes.Add("colspan");
        _sanitizer.AllowedAttributes.Add("rowspan");

        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("mailto");

        // Make links safe to open from the app.
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement a)
            {
                a.SetAttribute("target", "_blank");
                a.SetAttribute("rel", "noopener noreferrer");
            }
        };
    }

    public string? Sanitize(string? html) =>
        string.IsNullOrWhiteSpace(html) ? html : _sanitizer.Sanitize(html);
}
