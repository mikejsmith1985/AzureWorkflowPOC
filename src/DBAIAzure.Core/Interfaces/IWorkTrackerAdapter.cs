// The single seam between the pipeline/cost/binding layers and a work tracker (spec-018).
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Abstracts a work tracker (Azure DevOps, Jira, …) behind one contract so the pipeline, cost, and
/// binding layers contain no tracker-specific types (FR-001/009). One implementation per tracker. All
/// operations are best-effort — they log and return rather than throw into a pipeline run (FR-012).
/// </summary>
public interface IWorkTrackerAdapter
{
    /// <summary>Stable key identifying the tracker (e.g. "AzureDevOps", "Jira") — diagnostics + selection.</summary>
    string TrackerKey { get; }

    /// <summary>Creates a work item of the logical type, linked under <paramref name="parent"/> when set.</summary>
    Task<CreatedWorkItemRef> CreateWorkItemAsync(
        WorkItemType type, string title, string description,
        WorkItemRef? parent, CancellationToken cancellationToken = default);

    /// <summary>Refreshes an item's title/description and appends a comment (non-destructive); returns the ref.</summary>
    Task<CreatedWorkItemRef> UpsertWorkItemAsync(
        WorkItemRef item, string title, string description, string appendComment,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a discussion comment (append-only).</summary>
    Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets fields keyed by tracker-neutral <see cref="LogicalField"/> names; the adapter resolves each to
    /// its native reference. Writing <see cref="LogicalField.CostBindingKey"/> here is also what makes the
    /// binding key resolvable by <see cref="ResolveByBindingKeyAsync"/> on trackers that query a field.
    /// Unknown/unprovisioned fields are skipped and logged, never thrown.
    /// </summary>
    Task SetFieldsAsync(
        WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a supplied binding key to its work item; null when the key is unknown (unattributed).</summary>
    Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken cancellationToken = default);

    /// <summary>Ensures the logical fields exist and are usable on the relevant item types — idempotent.</summary>
    Task<ProvisioningResult> ProvisionFieldsAsync(
        AdoTelemetryFieldConfig fieldConfig, CancellationToken cancellationToken = default);

    /// <summary>How this tracker rolls cost up the hierarchy (native vs add-on vs none).</summary>
    RollupCapability GetRollupCapability();
}
