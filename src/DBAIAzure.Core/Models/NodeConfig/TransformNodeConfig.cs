// Realized configuration for a FunctionTransform node: how upstream data is reshaped for downstream.

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>
/// The executable configuration for a data-shaping step. Describes how upstream fields are mapped into
/// the shape the downstream node expects (FR-15.4).
/// </summary>
public sealed record TransformNodeConfig
{
    /// <summary>The ordered input→output field mappings applied to the incoming data.</summary>
    public IReadOnlyList<FieldMapping> FieldMappings { get; init; } = Array.Empty<FieldMapping>();
}
