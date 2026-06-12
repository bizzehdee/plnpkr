namespace PlanningPoker.Integrations;

/// <summary>Shared helpers for assembling the ticket description shown in the poker app. See #35.</summary>
internal static class TrackerDescription
{
    /// <summary>
    /// Appends an "Acceptance criteria" section to the description when the ticket has one. Both the
    /// description and the acceptance-criteria HTML must already be sanitised; the subtitle heading is
    /// trusted static markup.
    /// </summary>
    public static string? WithAcceptanceCriteria(string? description, string? acceptanceCriteriaHtml)
    {
        if (string.IsNullOrWhiteSpace(acceptanceCriteriaHtml))
        {
            return description;
        }

        var section = $"<h4>Acceptance criteria</h4>{acceptanceCriteriaHtml}";
        return string.IsNullOrWhiteSpace(description) ? section : $"{description}{section}";
    }
}
