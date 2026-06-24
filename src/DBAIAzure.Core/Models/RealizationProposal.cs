// A single not-yet-accepted candidate configuration for one node, shown to the user for review.

using DBAIAzure.Core.Models.NodeConfig;

namespace DBAIAzure.Core.Models;

/// <summary>The action a user takes on a realization proposal during review.</summary>
public enum RealizationDecision
{
    /// <summary>Accept the proposal as-is.</summary>
    Accept,

    /// <summary>Accept the proposal after editing one or more values.</summary>
    EditThenAccept,

    /// <summary>Reject the proposal (the node stays in its prior state).</summary>
    Reject,

    /// <summary>Discard this proposal and ask the assistant for a different one.</summary>
    Regenerate,
}

/// <summary>
/// One LLM-generated, not-yet-accepted candidate configuration for a single node, presented to the
/// user in plain language for accept / edit / reject / regenerate. Transient — it lives for the
/// duration of a review session and is never persisted; acceptance writes the config to the node.
/// </summary>
public sealed record RealizationProposal
{
    /// <summary>The node this proposal targets.</summary>
    public required string NodeId { get; init; }

    /// <summary>The category of the target node.</summary>
    public required WorkflowNodeType NodeType { get; init; }

    /// <summary>
    /// The candidate executable configuration. Null when <see cref="Status"/> is
    /// <see cref="NodeRealizationStatus.Blocked"/> or <see cref="NodeRealizationStatus.NeedsInput"/>.
    /// </summary>
    public RealizedNodeConfigEnvelope? ProposedConfig { get; init; }

    /// <summary>A plain-language description of what the node will do / use / produce, for review.</summary>
    public required string PlainLanguageSummary { get; init; }

    /// <summary>The status this proposal resolved to (Proposed, or Blocked / NeedsInput).</summary>
    public required NodeRealizationStatus Status { get; init; }

    /// <summary>Plain-language reason when the node is blocked or needs more input; otherwise null.</summary>
    public string? BlockingReason { get; init; }
}
