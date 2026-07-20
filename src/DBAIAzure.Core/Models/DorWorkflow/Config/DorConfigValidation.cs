// Validation rules for the DoR Validation Workflow configuration (spec-021), used by the config card's Check
// Health and by the resolver's callers to surface actionable misconfiguration before a run depends on it.
namespace DBAIAzure.Core.Models.DorWorkflow.Config;

/// <summary>
/// Pure validation of a <see cref="DorWorkflowConfig"/>. Returns a list of human-readable issues (empty when the
/// configuration is usable). Kept side-effect-free so it is trivially unit-testable and reusable by both the UI
/// health check and server-side guards.
/// </summary>
public static class DorConfigValidation
{
    /// <summary>Returns the configuration problems that would block a live run; empty when valid.</summary>
    public static IReadOnlyList<string> Validate(DorWorkflowConfig config)
    {
        var issues = new List<string>();

        // DoR document source seam: exactly the fields the chosen source_type needs must be present.
        switch (config.Dor.SourceType?.ToLowerInvariant())
        {
            case "inline" when string.IsNullOrWhiteSpace(config.Dor.InlineMarkdown):
                issues.Add("DoR source_type is 'inline' but inline_markdown is empty.");
                break;
            case "url" when string.IsNullOrWhiteSpace(config.Dor.SourceUri):
                issues.Add("DoR source_type is 'url' but source_uri is empty.");
                break;
            case null or "":
                issues.Add("DoR source_type is required (inline or url).");
                break;
        }

        // Business-hours SLA needs a non-empty working-days set to be measurable.
        if (string.Equals(config.Sla.ClockType, "business_hours", StringComparison.OrdinalIgnoreCase)
            && config.Sla.BusinessHours.WorkingDays.Count == 0)
        {
            issues.Add("SLA clock_type is 'business_hours' but no working_days are configured.");
        }

        // A transition id is required to advance a ticket to the ready status.
        if (string.IsNullOrWhiteSpace(config.Jira.ReadyTransitionId))
            issues.Add("jira.ready_transition_id is required to transition tickets to the ready status.");

        // At least one project must be monitored for the trigger to match anything.
        if (config.Jira.ProjectKeys.Count == 0)
            issues.Add("jira.project_keys is empty — no project would be monitored.");

        return issues;
    }
}
