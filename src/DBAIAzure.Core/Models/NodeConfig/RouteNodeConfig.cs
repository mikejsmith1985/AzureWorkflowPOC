// Realized configuration for a FunctionRoute node: the condition selecting each outgoing path.

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>
/// The executable configuration for a branching step. Carries one condition per outgoing port and a
/// guaranteed default path so the runtime always has a deterministic next step (FR-15.3).
/// </summary>
public sealed record RouteNodeConfig
{
    /// <summary>One condition per outgoing edge; the first satisfied condition selects the path.</summary>
    public IReadOnlyList<RouteCondition> Conditions { get; init; } = Array.Empty<RouteCondition>();

    /// <summary>The fallback output port followed when no condition is satisfied.</summary>
    public required string DefaultPortId { get; init; }
}
