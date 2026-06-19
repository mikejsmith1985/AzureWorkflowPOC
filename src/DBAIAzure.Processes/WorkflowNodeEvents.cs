// String event constants shared across all workflow node step types in the visual workflow builder pipeline.
#pragma warning disable SKEXP0080

namespace DBAIAzure.Processes;

/// <summary>
/// String event constants used by all workflow node step types.
/// Shared prefix prevents collisions with the existing intake pipeline events in Events.cs.
/// </summary>
public static class WorkflowNodeEvents
{
    // ── Per-node lifecycle events (sent to start or signal completion of each node) ──

    /// <summary>
    /// Signals a workflow node step to begin processing its assigned work unit.
    /// </summary>
    public const string NodeStart = "WorkflowNode.Start";

    /// <summary>
    /// Emitted by a node step when its work unit finishes successfully.
    /// </summary>
    public const string NodeCompleted = "WorkflowNode.Completed";

    /// <summary>
    /// Emitted by a node step when an unrecoverable error prevents completion.
    /// </summary>
    public const string NodeFailed = "WorkflowNode.Failed";

    /// <summary>
    /// Emitted when a node step is intentionally bypassed due to a conditional
    /// branch or a preceding failure that makes execution irrelevant.
    /// </summary>
    public const string NodeSkipped = "WorkflowNode.Skipped";

    // ── Human-in-the-loop (HITL) approval events ──

    /// <summary>
    /// Suspends the workflow at a node that requires explicit human sign-off before
    /// the pipeline may continue. The process resumes only after the corresponding
    /// <see cref="HumanApprovalReceived"/> event arrives via the external message channel.
    /// </summary>
    public const string AwaitHumanApproval = "WorkflowNode.AwaitHumanApproval";

    /// <summary>
    /// Delivered by the external channel once a human has reviewed and approved
    /// (or rejected) the suspended node, allowing the pipeline to resume.
    /// </summary>
    public const string HumanApprovalReceived = "WorkflowNode.HumanApprovalReceived";
}
