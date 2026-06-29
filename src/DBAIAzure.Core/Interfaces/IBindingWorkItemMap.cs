// Resolves a binding key to the work item it was minted for (remediation C1) — populated at creation.
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Persists the <c>bindingKey → workItemId</c> mapping the pipeline already knows at work-item creation,
/// so development-usage ingest resolves a supplied key locally instead of querying ADO per request. A
/// key absent from the map resolves to null → the ingest records the entry as unattributed (FR-010).
/// </summary>
public interface IBindingWorkItemMap
{
    /// <summary>Records the work item a binding key was minted for (called once at creation).</summary>
    Task PutAsync(string bindingKey, int workItemId, CancellationToken cancellationToken = default);

    /// <summary>Returns the work item for a binding key, or null when the key is unknown.</summary>
    Task<int?> ResolveAsync(string bindingKey, CancellationToken cancellationToken = default);
}
