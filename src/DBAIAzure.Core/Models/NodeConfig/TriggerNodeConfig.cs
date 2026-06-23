// Realized configuration for a Trigger node: what starts the workflow and the shape of its initial data.

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>
/// The executable configuration for the workflow entry point. Formalizes the previously ad-hoc
/// <c>{initialDataDescription}</c> blob the orchestrator already reads, and adds the structured shape
/// of the initial data handed to the first downstream node (FR-15.6).
/// </summary>
public sealed record TriggerNodeConfig
{
    /// <summary>Plain-language description of the real event/input that starts this workflow.</summary>
    public required string InitialDataDescription { get; init; }

    /// <summary>The structured shape of the initial data forwarded to the first runnable node.</summary>
    public IReadOnlyList<OutputField> OutputShape { get; init; } = Array.Empty<OutputField>();
}
