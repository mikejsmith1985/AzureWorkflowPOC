// EF entity mapping a binding key to the work item it was minted for (spec-017, remediation C1).
namespace DBAIAzure.Storage.Entities;

/// <summary>One row per binding key — resolves a supplied key to its anchor work item for dev-usage ingest.</summary>
public sealed class BindingWorkItemMapEntity
{
    /// <summary>Canonical binding key (primary key).</summary>
    public string BindingKey { get; set; } = string.Empty;

    /// <summary>The anchor work item the key was minted for — opaque ref (numeric ADO / string-key Jira).</summary>
    public string WorkItemId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
