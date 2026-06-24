// Root configuration record for the ADO telemetry field set — loaded from embedded resource or override file.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// Complete definition of all custom telemetry fields to create or map in ADO.
/// Loaded from the embedded "default-telemetry-config.json" resource; callers can
/// supply a custom instance to override per workflow or deployment.
/// The key in <see cref="WorkItemTypes"/> is a logical name ("UserStory", "Task")
/// resolved to the process-specific ADO WIT name based on <see cref="AdoProcessType"/>.
/// </summary>
public sealed record AdoTelemetryFieldConfig
{
    /// <summary>Semver string identifying the config schema version (e.g. "1.0").</summary>
    public required string Version { get; init; }

    /// <summary>
    /// All work item type groups keyed by logical name.
    /// Logical key "UserStory" → "User Story" (Agile) / "Product Backlog Item" (Scrum).
    /// </summary>
    public required IReadOnlyDictionary<string, AdoTelemetryWorkItemTypeConfig> WorkItemTypes { get; init; }

    /// <summary>
    /// Maps each <see cref="AdoFieldType"/> to a native ADO reference name used when
    /// Adaptive Mode cannot find an exact match. A null value means log-only for that type.
    /// </summary>
    public required IReadOnlyDictionary<AdoFieldType, string?> FallbackStrategy { get; init; }

    /// <summary>
    /// Encoding convention for multi-value telemetry written to System.Tags.
    /// Always "pipe_separated_kv" in the default config (e.g. "ai-session:abc | ai-model:claude-sonnet-4-6").
    /// </summary>
    public required string TagsEncoding { get; init; }
}
