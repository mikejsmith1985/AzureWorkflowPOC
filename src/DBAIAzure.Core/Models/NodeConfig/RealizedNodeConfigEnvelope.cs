// Wrapper that carries a node's realized, executable configuration plus the discriminator and
// version a runtime step needs to deserialize the correct type-specific shape.

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>
/// The serialized form stored in <see cref="WorkflowNode.FunctionConfig"/> once a node has been
/// realized. <see cref="NodeType"/> tells a consumer which type-specific record
/// <see cref="ConfigJson"/> deserializes to (see <c>NodeConfigSerializer</c>); <see cref="SchemaVersion"/>
/// guards forward migrations so older persisted workflows remain readable.
/// </summary>
public sealed record RealizedNodeConfigEnvelope
{
    /// <summary>Schema version of this envelope. Starts at 1; bump only on a breaking shape change.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>The node category whose type-specific config record is held in <see cref="ConfigJson"/>.</summary>
    public required WorkflowNodeType NodeType { get; init; }

    /// <summary>JSON of the type-specific config record (e.g. <c>NotifyNodeConfig</c>) for this node.</summary>
    public required string ConfigJson { get; init; }
}
