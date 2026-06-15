// Derives work item titles and descriptions from a feature's phase artifacts and validation result.
using DBAIAzure.Core.Models;
using System.Text;

namespace DBAIAzure.Web.Integrations.AzureDevOps;

/// <summary>
/// Translates phase state into the title and description fields for an Azure DevOps work item.
/// Content is derived from the artifacts and the validation summary — never free-form invented
/// (FR-009). User Story 1 covers the Specify→Epic mapping; later stories extend Plan/Implement.
/// </summary>
public static class WorkItemMapper
{
    /// <summary>Builds the Epic title for a Specify phase from the feature key.</summary>
    public static string BuildEpicTitle(PhaseHandlerState state) =>
        $"[Epic] {state.FeatureKey}";

    /// <summary>
    /// Builds the title for the phase's primary work item. The Specify Epic summarises the whole
    /// feature; the Implement Bug/completion record names the feature and phase. (Plan produces one
    /// Task per planned unit instead — see <see cref="BuildPlannedTaskTitle"/>.)
    /// </summary>
    public static string BuildTitle(PhaseHandlerState state)
    {
        var workItemType = PhaseWorkItemMap.ToWorkItemType(state.Phase) ?? "Item";
        return state.Phase == SpecKitPhase.Specify
            ? $"[{workItemType}] {state.FeatureKey}"
            : $"[{workItemType}] {state.FeatureKey} — {state.Phase}";
    }

    /// <summary>
    /// Builds the title for a single Plan-phase child Task from a parsed planned unit of work,
    /// prefixed with the feature key so the board reads coherently (FR-009).
    /// </summary>
    public static string BuildPlannedTaskTitle(PhaseHandlerState state, PlannedWorkItem plannedItem) =>
        $"[{PhaseWorkItemMap.TaskType}] {state.FeatureKey} — {plannedItem.Title}";

    /// <summary>Builds a child Task description from the planned unit of work's parsed body (FR-009).</summary>
    public static string BuildPlannedTaskDescription(PlannedWorkItem plannedItem) =>
        plannedItem.Description;

    /// <summary>Builds the work item description from the validation summary and flagged gaps.</summary>
    public static string BuildDescription(PhaseHandlerState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine(state.Validation?.Summary ?? "No summary available.");

        var gaps = state.Validation?.Gaps ?? [];
        if (gaps.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Flagged gaps:");
            foreach (var gap in gaps)
                builder.AppendLine($"- {gap.Label}: {gap.Description}");
        }

        return builder.ToString().TrimEnd();
    }
}
