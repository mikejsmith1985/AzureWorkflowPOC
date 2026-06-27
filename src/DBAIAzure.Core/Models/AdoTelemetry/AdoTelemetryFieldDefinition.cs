// Describes a single custom ADO telemetry field that the preflight service creates or maps.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// Defines one custom telemetry field that the preflight service either creates in ADO
/// (Bootstrap Mode) or maps to a native ADO field (Adaptive Mode).
/// Invariant: <see cref="PicklistValues"/> must be non-null and non-empty when
/// <see cref="FieldType"/> is <see cref="AdoFieldType.PicklistString"/>.
/// </summary>
public sealed record AdoTelemetryFieldDefinition
{
    /// <summary>Human-readable display name shown in ADO work item forms.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// ADO reference name used in REST calls (e.g. "Custom.AISessionID").
    /// Convention: all custom fields use the "Custom." prefix.
    /// </summary>
    public required string ReferenceName { get; init; }

    /// <summary>The data type of this field — drives ADO type string and picklist creation.</summary>
    public required AdoFieldType FieldType { get; init; }

    /// <summary>
    /// Allowed values when <see cref="FieldType"/> is <see cref="AdoFieldType.PicklistString"/>.
    /// Null for all other field types.
    /// </summary>
    public IReadOnlyList<string>? PicklistValues { get; init; }

    /// <summary>Whether this field must be present for the pipeline to write telemetry data.</summary>
    public required bool Required { get; init; }

    /// <summary>
    /// Native ADO reference name to use in Adaptive Mode as the fallback write target
    /// (e.g. "System.Tags", "Microsoft.VSTS.Scheduling.StoryPoints").
    /// Null means the field is log-only — its value is logged but never written to ADO.
    /// </summary>
    public string? FallbackReferenceName { get; init; }

    /// <summary>Human-readable display name of <see cref="FallbackReferenceName"/> for UI/logging.</summary>
    public string? FallbackDisplayName { get; init; }
}
