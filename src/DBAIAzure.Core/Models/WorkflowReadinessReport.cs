// The aggregate production-readiness assessment of a workflow plus per-node detail.

namespace DBAIAzure.Core.Models;

/// <summary>The realization readiness of one node, with plain-language reasons when not ready.</summary>
public sealed record NodeReadiness
{
    /// <summary>The node assessed.</summary>
    public required string NodeId { get; init; }

    /// <summary>The node's computed realization status.</summary>
    public required NodeRealizationStatus Status { get; init; }

    /// <summary>Plain-language reasons the node is not yet realized; empty when realized.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The result of evaluating a workflow for production-readiness: an overall verdict plus per-node
/// status and the top-level reasons the run action is gated. The workflow is ready only when every
/// node is realized (FR-16.4, FR-17).
/// </summary>
public sealed record WorkflowReadinessReport
{
    /// <summary>True only when every node is <see cref="NodeRealizationStatus.Realized"/>.</summary>
    public required bool IsProductionReady { get; init; }

    /// <summary>Per-node readiness detail.</summary>
    public IReadOnlyList<NodeReadiness> Nodes { get; init; } = Array.Empty<NodeReadiness>();

    /// <summary>Plain-language reasons the workflow is not production-ready (empty when ready).</summary>
    public IReadOnlyList<string> BlockingSummary { get; init; } = Array.Empty<string>();
}
