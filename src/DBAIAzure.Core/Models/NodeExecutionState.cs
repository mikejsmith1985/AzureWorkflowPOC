// Immutable snapshot of a single workflow node's execution state, used to drive
// the visual workflow builder's per-node status badges and expanded output panels.
namespace DBAIAzure.Core.Models;

/// <summary>
/// Immutable snapshot of one node's execution within a workflow run. Every state
/// transition produces a new instance via a <c>with</c> expression — never mutated in
/// place — so the UI can diff successive snapshots to animate transitions.
/// </summary>
public sealed record NodeExecutionState
{
    /// <summary>
    /// Stable identifier that matches the node's id in the workflow definition, used to
    /// correlate this snapshot with the correct canvas node in the visual builder.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Current lifecycle status; drives the badge colour and icon rendered on the canvas node.
    /// </summary>
    public required NodeStatus Status { get; init; }

    /// <summary>
    /// Short plain-language summary (one sentence, ≤120 chars) shown as an inline badge
    /// beneath the canvas node when status is Completed or Failed (FR-07.3).
    /// </summary>
    public string? OutputSummary { get; init; }

    /// <summary>
    /// Complete LLM or step output for display in the expanded run-output panel.
    /// Null while the node has not yet produced output.
    /// </summary>
    public string? FullOutput { get; init; }

    /// <summary>
    /// Human-readable explanation of why the node failed, shown in the error panel.
    /// Null for any non-failed terminal status.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>UTC timestamp recorded the moment execution began.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>UTC timestamp recorded the moment execution reached a terminal status.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Factory helper: create a node state record initialised to <see cref="NodeStatus.NotStarted"/>.</summary>
    public static NodeExecutionState CreateInitial(string nodeId) =>
        new() { NodeId = nodeId, Status = NodeStatus.NotStarted };
}
