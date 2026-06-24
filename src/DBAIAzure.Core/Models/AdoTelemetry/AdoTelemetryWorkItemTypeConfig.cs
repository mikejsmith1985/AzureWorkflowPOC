// Groups telemetry field definitions by their target ADO work item type.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// The set of custom telemetry fields that should be created on (or mapped from)
/// a specific ADO work item type. The config key (e.g. "UserStory", "Task") is
/// resolved to the process-specific ADO WIT name at runtime.
/// </summary>
public sealed record AdoTelemetryWorkItemTypeConfig
{
    /// <summary>
    /// Actual ADO work item type name as it appears in ADO
    /// (e.g. "User Story" for Agile, "Product Backlog Item" for Scrum).
    /// </summary>
    public required string WorkItemTypeName { get; init; }

    /// <summary>All telemetry fields to create or map for this work item type.</summary>
    public required IReadOnlyList<AdoTelemetryFieldDefinition> Fields { get; init; }
}
